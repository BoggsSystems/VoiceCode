using System.Text;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using VoiceCode.WorkerService.Configuration;
using VoiceCode.WorkerService.Models;

namespace VoiceCode.WorkerService.Services;

public class WorkerQueueProcessorService : BackgroundService
{
    private readonly ILogger<WorkerQueueProcessorService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly WorkerOptions _options;
    private ServiceBusClient? _serviceBusClient;
    private ServiceBusProcessor? _processor;
    private readonly string _queueName;

    public WorkerQueueProcessorService(
        ILogger<WorkerQueueProcessorService> logger,
        IServiceProvider serviceProvider,
        IOptions<WorkerOptions> options)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _options = options.Value;
        
        // Get WORKER_ID from environment variable and construct queue name
        var workerId = Environment.GetEnvironmentVariable("WORKER_ID");
        if (!string.IsNullOrEmpty(workerId))
        {
            _queueName = $"worker-{workerId}-tasks";
            _logger.LogInformation($"Worker {workerId} will listen to queue: {_queueName}");
        }
        else
        {
            _queueName = "worker-tasks";
            _logger.LogWarning("WORKER_ID not set. Using default queue: worker-tasks");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Initialize Service Bus client
            var connectionString = Environment.GetEnvironmentVariable("AZURE_SERVICE_BUS_CONNECTION_STRING");
            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogWarning("Service Bus connection string not found. Queue processing disabled.");
                return;
            }

            _serviceBusClient = new ServiceBusClient(connectionString);
            _logger.LogInformation($"Connecting to Service Bus queue: {_queueName}");
            _processor = _serviceBusClient.CreateProcessor(_queueName, new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = _options.MaxConcurrentWorkers,
                AutoCompleteMessages = false
            });

            _processor.ProcessMessageAsync += ProcessMessageAsync;
            _processor.ProcessErrorAsync += ProcessErrorAsync;

            await _processor.StartProcessingAsync(stoppingToken);
            _logger.LogInformation("Worker queue processor started");

            // Keep the service running
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in worker queue processor");
        }
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var messageId = args.Message.MessageId;
        var sequenceNumber = args.Message.SequenceNumber;
        
        _logger.LogInformation("=== WORKER MESSAGE RECEIVED ===");
        _logger.LogInformation("Message ID: {MessageId}, Sequence: {SequenceNumber}, Size: {Size} bytes", 
            messageId, sequenceNumber, args.Message.Body.ToArray().Length);
        
        try
        {
            var body = Encoding.UTF8.GetString(args.Message.Body);
            _logger.LogInformation("Raw message body: {Body}", body.Length > 500 ? body.Substring(0, 500) + "..." : body);
            
            var workerPayload = JsonConvert.DeserializeObject<WorkerTaskPayload>(body);
            
            if (workerPayload == null)
            {
                _logger.LogError("Failed to deserialize task message. Body was: {Body}", body);
                await args.CompleteMessageAsync(args.Message);
                return;
            }

            _logger.LogInformation("Deserialized WorkerTaskPayload - TaskId: {TaskId}, Command: {Command}, Context.WorkerNumber: {WorkerNumber}", 
                workerPayload.TaskId, 
                workerPayload.Command?.Length > 100 ? workerPayload.Command.Substring(0, 100) + "..." : workerPayload.Command,
                workerPayload.Context?.WorkerNumber);

            // Check if Command is empty
            if (string.IsNullOrEmpty(workerPayload.Command))
            {
                _logger.LogError("Voice command is empty for task {TaskId}. Full payload: {Payload}", 
                    workerPayload.TaskId, JsonConvert.SerializeObject(workerPayload));
                
                var errorResult = new WorkerTaskResult
                {
                    TaskId = workerPayload.TaskId,
                    Success = false,
                    Summary = "Voice command is required",
                    Error = "Voice command is required",
                    Metadata = new Dictionary<string, object>
                    {
                        ["worker_id"] = Environment.GetEnvironmentVariable("WORKER_ID") ?? "unknown",
                        ["timestamp"] = DateTime.UtcNow,
                        ["error_type"] = "empty_command"
                    }
                };
                
                _logger.LogInformation("Sending error result for empty command");
                await SendResultAsync(errorResult);
                await args.CompleteMessageAsync(args.Message);
                return;
            }

            _logger.LogInformation("=== PROCESSING TASK ===");
            _logger.LogInformation("Task ID: {TaskId}", workerPayload.TaskId);
            _logger.LogInformation("Command: {Command}", workerPayload.Command);
            _logger.LogInformation("Session ID: {SessionId}", workerPayload.Context?.SessionId);
            _logger.LogInformation("User ID: {UserId}", workerPayload.Context?.UserId);
            _logger.LogInformation("Original Transcription: {OriginalTranscription}", workerPayload.Context?.OriginalTranscription);

            // Convert to WorkerTask format
            var workerTask = new WorkerTask
            {
                Id = workerPayload.TaskId,
                Type = "voice_command",
                Description = workerPayload.Command,
                WorkspaceId = workerPayload.Context?.WorkerNumber.ToString() ?? "1",
                Parameters = new Dictionary<string, object>
                {
                    ["voiceCommand"] = workerPayload.Command,
                    ["sessionId"] = workerPayload.Context?.SessionId ?? "",
                    ["userId"] = workerPayload.Context?.UserId ?? "",
                    ["workerNumber"] = workerPayload.Context?.WorkerNumber ?? 1,
                    ["originalTranscription"] = workerPayload.Context?.OriginalTranscription ?? workerPayload.Command
                },
                CreatedAt = DateTime.UtcNow
            };

            _logger.LogInformation("Created WorkerTask - Id: {Id}, Type: {Type}, WorkspaceId: {WorkspaceId}", 
                workerTask.Id, workerTask.Type, workerTask.WorkspaceId);

            using var scope = _serviceProvider.CreateScope();
            var workerService = scope.ServiceProvider.GetRequiredService<IClaudeCodeWorkerService>();
            
            _logger.LogInformation("Executing task with ClaudeCodeWorkerService");
            
            // Execute the task
            var result = await workerService.ExecuteTaskAsync(workerTask);

            _logger.LogInformation("Task execution completed - Success: {Success}, Summary: {Summary}, Error: {Error}", 
                result.Success, 
                result.Summary?.Length > 100 ? result.Summary.Substring(0, 100) + "..." : result.Summary,
                result.Error);
            
            // Add worker metadata to result
            if (result.Metadata == null)
            {
                result.Metadata = new Dictionary<string, object>();
            }
            result.Metadata["worker_id"] = Environment.GetEnvironmentVariable("WORKER_ID") ?? "unknown";
            result.Metadata["processed_at"] = DateTime.UtcNow;
            result.Metadata["original_command"] = workerPayload.Command;
            result.Metadata["sessionId"] = workerPayload.Context?.SessionId ?? "";
            result.Metadata["voiceCommand"] = workerPayload.Command;

            _logger.LogInformation("Result metadata - SessionId: {SessionId}, WorkerId: {WorkerId}", 
                result.Metadata["sessionId"], result.Metadata["worker_id"]);
            _logger.LogInformation("Sending result to response queue");
            // Send result to response queue
            await SendResultAsync(result);

            // Complete the message
            await args.CompleteMessageAsync(args.Message);
            
            _logger.LogInformation("=== TASK COMPLETED SUCCESSFULLY ===");
            _logger.LogInformation("Task {TaskId} processed successfully", workerPayload.TaskId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "=== ERROR PROCESSING MESSAGE ===");
            _logger.LogError("Exception Type: {ExceptionType}", ex.GetType().Name);
            _logger.LogError("Exception Message: {ExceptionMessage}", ex.Message);
            _logger.LogError("Stack Trace: {StackTrace}", ex.StackTrace);
            
            try
            {
                // Try to send error result
                var body = Encoding.UTF8.GetString(args.Message.Body);
                var workerPayload = JsonConvert.DeserializeObject<WorkerTaskPayload>(body);
                
                if (workerPayload != null)
                {
                    var errorResult = new WorkerTaskResult
                    {
                        TaskId = workerPayload.TaskId,
                        Success = false,
                        Summary = $"Worker encountered an error: {ex.Message}",
                        Error = ex.ToString(),
                        Metadata = new Dictionary<string, object>
                        {
                            ["worker_id"] = Environment.GetEnvironmentVariable("WORKER_ID") ?? "unknown",
                            ["error_type"] = ex.GetType().Name,
                            ["timestamp"] = DateTime.UtcNow
                        }
                    };
                    
                    await SendResultAsync(errorResult);
                }
            }
            catch (Exception innerEx)
            {
                _logger.LogError(innerEx, "Failed to send error result");
            }
            
            // Abandon the message for retry
            await args.AbandonMessageAsync(args.Message);
        }
    }

    private async Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Error in Service Bus processor. Source: {Source}", args.ErrorSource);
    }

    private async Task SendResultAsync(WorkerTaskResult result)
    {
        try
        {
            var resultQueue = "worker-results";
            _logger.LogInformation("Preparing to send result to queue: {Queue}", resultQueue);
            
            var sender = _serviceBusClient!.CreateSender(resultQueue);
            
            var messageBody = JsonConvert.SerializeObject(result);
            _logger.LogInformation("Serialized result message: {MessageBody}", 
                messageBody.Length > 500 ? messageBody.Substring(0, 500) + "..." : messageBody);
            
            var message = new ServiceBusMessage(Encoding.UTF8.GetBytes(messageBody))
            {
                ContentType = "application/json",
                Subject = result.TaskId,
                MessageId = Guid.NewGuid().ToString()
            };
            
            // Add metadata to message properties
            message.ApplicationProperties["WorkerId"] = Environment.GetEnvironmentVariable("WORKER_ID") ?? "unknown";
            message.ApplicationProperties["Success"] = result.Success;
            message.ApplicationProperties["Timestamp"] = DateTime.UtcNow.ToString("O");

            _logger.LogInformation("Sending message with ID: {MessageId}, Subject: {Subject}", 
                message.MessageId, message.Subject);
            
            await sender.SendMessageAsync(message);
            await sender.DisposeAsync();
            
            _logger.LogInformation("Successfully sent result for task {TaskId} to result queue. Success: {Success}, Summary: {Summary}", 
                result.TaskId, result.Success, result.Summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send task result for task {TaskId}", result.TaskId);
            _logger.LogError("Result that failed to send: {Result}", JsonConvert.SerializeObject(result));
            throw; // Re-throw to ensure the message gets retried
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping worker queue processor");
        
        if (_processor != null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }

        if (_serviceBusClient != null)
        {
            await _serviceBusClient.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}