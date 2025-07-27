using System.Text;
using Azure.Messaging.ServiceBus;
using Newtonsoft.Json;
using VoiceCode.VoiceIntelligenceService.Models;

namespace VoiceCode.VoiceIntelligenceService.Services;

public class ResultProcessorService : BackgroundService
{
    private readonly ILogger<ResultProcessorService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private ServiceBusClient? _serviceBusClient;
    private ServiceBusProcessor? _processor;
    private readonly Dictionary<string, List<WorkerResult>> _taskResults = new();
    private readonly Dictionary<string, TaskCompletionSource<VoiceResponse>> _pendingTasks = new();

    public ResultProcessorService(
        ILogger<ResultProcessorService> logger,
        IServiceProvider serviceProvider,
        IConfiguration configuration)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Initialize Service Bus client
            var connectionString = _configuration["ServiceBus:ConnectionString"] ?? 
                Environment.GetEnvironmentVariable("AZURE_SERVICE_BUS_CONNECTION_STRING");
            
            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogError("Service Bus connection string not found. Result processing disabled.");
                return;
            }
            
            _logger.LogInformation("Initializing Service Bus client...");
            _serviceBusClient = new ServiceBusClient(connectionString);
            
            _processor = _serviceBusClient.CreateProcessor("worker-results", new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = 5,
                AutoCompleteMessages = false
            });

            _processor.ProcessMessageAsync += ProcessWorkerResultAsync;
            _processor.ProcessErrorAsync += ProcessErrorAsync;

            await _processor.StartProcessingAsync(stoppingToken);
            _logger.LogInformation("Result processor started, listening to worker-results queue");

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start result processor");
            throw;
        }
    }

    private async Task ProcessWorkerResultAsync(ProcessMessageEventArgs args)
    {
        var receivedAt = DateTime.UtcNow;
        var messageId = args.Message.MessageId;
        
        _logger.LogInformation("Worker Result Received - MessageId: {MessageId}, SequenceNumber: {SequenceNumber}, EnqueuedTime: {EnqueuedTime}",
            messageId, args.Message.SequenceNumber, args.Message.EnqueuedTime);
        
        try
        {
            var body = Encoding.UTF8.GetString(args.Message.Body);
            var result = JsonConvert.DeserializeObject<WorkerTaskResult>(body);
            
            if (result == null)
            {
                _logger.LogError("Failed to deserialize worker result - MessageId: {MessageId}", messageId);
                await args.CompleteMessageAsync(args.Message);
                return;
            }

            var workerId = result.Metadata?.ContainsKey("worker_id") == true ? 
                result.Metadata["worker_id"]?.ToString() : "unknown";
            var sessionId = result.Metadata?.ContainsKey("sessionId") == true ? 
                result.Metadata["sessionId"]?.ToString() : "none";
            
            _logger.LogInformation("Worker Result Details - TaskId: {TaskId}, WorkerId: {WorkerId}, SessionId: {SessionId}, Success: {Success}, HasError: {HasError}", 
                result.TaskId, workerId, sessionId, result.Success, !string.IsNullOrEmpty(result.Error));

            // Convert to our model
            var workerResult = new WorkerResult
            {
                TaskId = result.TaskId,
                WorkerId = result.Metadata?.ContainsKey("worker_id") == true ? 
                    result.Metadata["worker_id"]?.ToString() ?? "unknown" : "unknown",
                Success = result.Success,
                Output = result.Summary,
                Error = result.Error ?? "",
                Metadata = result.Metadata
            };

            // Store result and check if we need to synthesize
            _logger.LogDebug("Storing worker result for TaskId: {TaskId}", result.TaskId);
            await StoreAndProcessResult(workerResult);

            await args.CompleteMessageAsync(args.Message);
            
            var processingDuration = (DateTime.UtcNow - receivedAt).TotalMilliseconds;
            _logger.LogInformation("Worker Result Processed - MessageId: {MessageId}, TaskId: {TaskId}, ProcessingTime: {Time}ms",
                messageId, result.TaskId, processingDuration);
        }
        catch (Exception ex)
        {
            var failureDuration = (DateTime.UtcNow - receivedAt).TotalMilliseconds;
            _logger.LogError(ex, "Worker Result Processing Failed - MessageId: {MessageId}, ProcessingTime: {Time}ms",
                messageId, failureDuration);
            await args.AbandonMessageAsync(args.Message);
        }
    }

    private async Task StoreAndProcessResult(WorkerResult result)
    {
        lock (_taskResults)
        {
            if (!_taskResults.ContainsKey(result.TaskId))
            {
                _taskResults[result.TaskId] = new List<WorkerResult>();
            }
            _taskResults[result.TaskId].Add(result);
        }

        // For now, synthesize after each result
        // In production, might wait for all expected workers or timeout
        await SynthesizeResponseAsync(result.TaskId);
    }

    private async Task SynthesizeResponseAsync(string taskId)
    {
        var synthesisStartTime = DateTime.UtcNow;
        
        _logger.LogInformation("Starting Voice Response Synthesis - TaskId: {TaskId}, Timestamp: {Timestamp}",
            taskId, synthesisStartTime);
        
        try
        {
            List<WorkerResult> results;
            lock (_taskResults)
            {
                if (!_taskResults.ContainsKey(taskId))
                {
                    _logger.LogWarning("No results found for TaskId: {TaskId}, skipping synthesis", taskId);
                    return;
                }
                    
                results = _taskResults[taskId].ToList();
                _logger.LogInformation("Found {Count} worker results for TaskId: {TaskId}",
                    results.Count, taskId);
            }

            using var scope = _serviceProvider.CreateScope();
            var synthesisService = scope.ServiceProvider.GetRequiredService<IOpenAISynthesisService>();
            var ttsQueueService = scope.ServiceProvider.GetRequiredService<ITTSQueueService>();

            // Get original command and session ID from metadata
            var firstResult = results.FirstOrDefault();
            var originalCommand = "unknown command";
            var sessionId = "";
            
            if (firstResult?.Metadata != null)
            {
                if (firstResult.Metadata.ContainsKey("voiceCommand"))
                {
                    originalCommand = firstResult.Metadata["voiceCommand"]?.ToString() ?? "unknown command";
                }
                if (firstResult.Metadata.ContainsKey("sessionId"))
                {
                    sessionId = firstResult.Metadata["sessionId"]?.ToString() ?? "";
                }
                
                _logger.LogInformation("Extracted from metadata - SessionId: {SessionId}, OriginalCommand: {Command}", 
                    sessionId, originalCommand);
            }
            else
            {
                _logger.LogWarning("No metadata found in first result for task {TaskId}", taskId);
            }

            var synthesisRequest = new SynthesisRequest
            {
                TaskId = taskId,
                OriginalVoiceCommand = originalCommand,
                WorkerResults = results,
                Context = new ConversationContext
                {
                    SessionId = sessionId,
                    OriginalCommand = originalCommand,
                    WorkerResults = results,
                    StartTime = DateTime.UtcNow
                }
            };
            
            _logger.LogInformation("Calling OpenAI Synthesis Service - TaskId: {TaskId}, SessionId: {SessionId}, Workers: {Workers}",
                taskId, sessionId, string.Join(", ", results.Select(r => r.WorkerId)));

            var openAISynthesisStartTime = DateTime.UtcNow;
            var voiceResponse = await synthesisService.SynthesizeResponseAsync(synthesisRequest);
            var openAISynthesisDuration = (DateTime.UtcNow - openAISynthesisStartTime).TotalMilliseconds;
            
            _logger.LogInformation("OpenAI Synthesis Complete - TaskId: {TaskId}, ResponseLength: {Length}, SynthesisTime: {Time}ms",
                taskId, voiceResponse.SpokenResponse?.Length ?? 0, openAISynthesisDuration);
            
            // Log the voice response details
            var responsePreview = voiceResponse.SpokenResponse?.Length > 100 
                ? voiceResponse.SpokenResponse.Substring(0, 100) + "..." 
                : voiceResponse.SpokenResponse;
            _logger.LogDebug("Voice Response Preview - TaskId: {TaskId}, Response: {Response}",
                taskId, responsePreview);
            
            // Send to TTS queue
            _logger.LogInformation("Sending to TTS Queue - TaskId: {TaskId}, SessionId: {SessionId}",
                taskId, voiceResponse.SessionId);
            
            var ttsQueueStartTime = DateTime.UtcNow;
            await ttsQueueService.SendToTTSAsync(voiceResponse);
            var ttsQueueDuration = (DateTime.UtcNow - ttsQueueStartTime).TotalMilliseconds;
            
            var totalDuration = (DateTime.UtcNow - synthesisStartTime).TotalMilliseconds;
            _logger.LogInformation("Voice Response Pipeline Complete - TaskId: {TaskId}, SessionId: {SessionId}, TotalTime: {TotalTime}ms, OpenAITime: {OpenAI}ms, QueueTime: {Queue}ms", 
                taskId, sessionId, totalDuration, openAISynthesisDuration, ttsQueueDuration);

            // Clean up
            lock (_taskResults)
            {
                _taskResults.Remove(taskId);
                _logger.LogDebug("Cleaned up results for TaskId: {TaskId}", taskId);
            }
        }
        catch (Exception ex)
        {
            var failureDuration = (DateTime.UtcNow - synthesisStartTime).TotalMilliseconds;
            _logger.LogError(ex, "Voice Response Synthesis Failed - TaskId: {TaskId}, FailureTime: {Time}ms", 
                taskId, failureDuration);
        }
    }

    private async Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Error in Service Bus processor");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
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

// Model from worker service
public class WorkerTaskResult
{
    public string TaskId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? Error { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}