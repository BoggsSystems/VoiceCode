using System.Text;
using Azure.Messaging.ServiceBus;
using Newtonsoft.Json;
using VoiceCode.VoiceIntelligenceService.Models;

namespace VoiceCode.VoiceIntelligenceService.Services;

public class ResultProcessorService : BackgroundService
{
    private readonly ILogger<ResultProcessorService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ServiceBusClient _serviceBusClient;
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
        
        var connectionString = configuration["ServiceBus:ConnectionString"] ?? 
            Environment.GetEnvironmentVariable("AZURE_SERVICE_BUS_CONNECTION_STRING");
        
        _serviceBusClient = new ServiceBusClient(connectionString);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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

    private async Task ProcessWorkerResultAsync(ProcessMessageEventArgs args)
    {
        try
        {
            var body = Encoding.UTF8.GetString(args.Message.Body);
            var result = JsonConvert.DeserializeObject<WorkerTaskResult>(body);
            
            if (result == null)
            {
                _logger.LogError("Failed to deserialize worker result");
                await args.CompleteMessageAsync(args.Message);
                return;
            }

            _logger.LogInformation("Processing result for task {TaskId} from worker {WorkerId}", 
                result.TaskId, result.Metadata?["workerId"]);

            // Convert to our model
            var workerResult = new WorkerResult
            {
                TaskId = result.TaskId,
                WorkerId = result.Metadata?["workerId"]?.ToString() ?? "unknown",
                Success = result.Success,
                Output = result.Summary,
                Error = result.Error ?? "",
                Metadata = result.Metadata
            };

            // Store result and check if we need to synthesize
            await StoreAndProcessResult(workerResult);

            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing worker result");
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
        try
        {
            List<WorkerResult> results;
            lock (_taskResults)
            {
                if (!_taskResults.ContainsKey(taskId))
                    return;
                    
                results = _taskResults[taskId].ToList();
            }

            using var scope = _serviceProvider.CreateScope();
            var synthesisService = scope.ServiceProvider.GetRequiredService<IOpenAISynthesisService>();
            var ttsQueueService = scope.ServiceProvider.GetRequiredService<ITTSQueueService>();

            // Get original command from metadata (this would come from orchestrator in production)
            var originalCommand = results.FirstOrDefault()?.Metadata?["voiceCommand"]?.ToString() ?? "unknown command";

            var synthesisRequest = new SynthesisRequest
            {
                TaskId = taskId,
                OriginalVoiceCommand = originalCommand,
                WorkerResults = results
            };

            var voiceResponse = await synthesisService.SynthesizeResponseAsync(synthesisRequest);
            
            // Send to TTS queue
            await ttsQueueService.SendToTTSAsync(voiceResponse);
            
            _logger.LogInformation("Synthesized and queued voice response for task {TaskId}", taskId);

            // Clean up
            lock (_taskResults)
            {
                _taskResults.Remove(taskId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to synthesize response for task {TaskId}", taskId);
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

        await _serviceBusClient.DisposeAsync();
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