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
    private readonly string _queueName = "worker-tasks";

    public WorkerQueueProcessorService(
        ILogger<WorkerQueueProcessorService> logger,
        IServiceProvider serviceProvider,
        IOptions<WorkerOptions> options)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _options = options.Value;
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
        try
        {
            var body = Encoding.UTF8.GetString(args.Message.Body);
            var task = JsonConvert.DeserializeObject<WorkerTask>(body);
            
            if (task == null)
            {
                _logger.LogError("Failed to deserialize task message");
                await args.CompleteMessageAsync(args.Message);
                return;
            }

            _logger.LogInformation("Processing task {TaskId} of type {TaskType}", task.Id, task.Type);

            using var scope = _serviceProvider.CreateScope();
            var workerService = scope.ServiceProvider.GetRequiredService<IClaudeCodeWorkerService>();
            
            // Execute the task
            var result = await workerService.ExecuteTaskAsync(task);

            // Send result to response queue
            await SendResultAsync(result);

            // Complete the message
            await args.CompleteMessageAsync(args.Message);
            
            _logger.LogInformation("Task {TaskId} processed successfully", task.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message");
            
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
            var sender = _serviceBusClient!.CreateSender(resultQueue);
            
            var messageBody = JsonConvert.SerializeObject(result);
            var message = new ServiceBusMessage(Encoding.UTF8.GetBytes(messageBody))
            {
                ContentType = "application/json",
                Subject = result.TaskId,
                MessageId = Guid.NewGuid().ToString()
            };

            await sender.SendMessageAsync(message);
            _logger.LogInformation("Sent result for task {TaskId} to result queue", result.TaskId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send task result");
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