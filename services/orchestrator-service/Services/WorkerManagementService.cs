using System.Collections.Concurrent;
using System.Text;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using VoiceCode.OrchestratorService.Configuration;
using VoiceCode.OrchestratorService.Models;

namespace VoiceCode.OrchestratorService.Services;

public interface IWorkerManagementService
{
    Task<string> SubmitTaskAsync(WorkerTask task);
    Task<WorkerTaskResult?> GetTaskResultAsync(string taskId, TimeSpan timeout);
    Task<List<WorkerStatus>> GetWorkerStatusesAsync();
    Task<bool> IsWorkerAvailableAsync();
}

public class WorkerManagementService : IWorkerManagementService, IDisposable
{
    private readonly ILogger<WorkerManagementService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OrchestrationOptions _options;
    private readonly ServiceBusClient _serviceBusClient;
    private readonly ServiceBusSender _taskSender;
    private readonly ServiceBusReceiver _resultReceiver;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<WorkerTaskResult>> _pendingResults;

    public WorkerManagementService(
        ILogger<WorkerManagementService> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<OrchestrationOptions> options)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _pendingResults = new ConcurrentDictionary<string, TaskCompletionSource<WorkerTaskResult>>();

        // Initialize Service Bus
        var connectionString = Environment.GetEnvironmentVariable("AZURE_SERVICE_BUS_CONNECTION_STRING");
        if (!string.IsNullOrEmpty(connectionString))
        {
            _serviceBusClient = new ServiceBusClient(connectionString);
            _taskSender = _serviceBusClient.CreateSender("worker-tasks");
            _resultReceiver = _serviceBusClient.CreateReceiver("worker-results");
            
            // Start listening for results
            _ = Task.Run(ProcessResultsAsync);
        }
        else
        {
            _logger.LogWarning("Service Bus connection string not configured");
        }
    }

    public async Task<string> SubmitTaskAsync(WorkerTask task)
    {
        try
        {
            _logger.LogInformation("Submitting task {TaskId} of type {TaskType}", task.Id, task.Type);

            // Create a completion source for this task
            var tcs = new TaskCompletionSource<WorkerTaskResult>();
            _pendingResults[task.Id] = tcs;

            // Send task to queue
            var messageBody = JsonConvert.SerializeObject(task);
            var message = new ServiceBusMessage(Encoding.UTF8.GetBytes(messageBody))
            {
                ContentType = "application/json",
                Subject = task.Type,
                MessageId = task.Id,
                TimeToLive = TimeSpan.FromMinutes(_options.TaskTimeoutMinutes)
            };

            await _taskSender.SendMessageAsync(message);
            _logger.LogInformation("Task {TaskId} submitted to queue", task.Id);

            return task.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit task {TaskId}", task.Id);
            _pendingResults.TryRemove(task.Id, out _);
            throw;
        }
    }

    public async Task<WorkerTaskResult?> GetTaskResultAsync(string taskId, TimeSpan timeout)
    {
        if (_pendingResults.TryGetValue(taskId, out var tcs))
        {
            using var cts = new CancellationTokenSource(timeout);
            using (cts.Token.Register(() => tcs.TrySetCanceled()))
            {
                try
                {
                    return await tcs.Task;
                }
                catch (TaskCanceledException)
                {
                    _logger.LogWarning("Timeout waiting for task {TaskId} result", taskId);
                    return null;
                }
                finally
                {
                    _pendingResults.TryRemove(taskId, out _);
                }
            }
        }

        _logger.LogWarning("No pending result found for task {TaskId}", taskId);
        return null;
    }

    public async Task<List<WorkerStatus>> GetWorkerStatusesAsync()
    {
        var statuses = new List<WorkerStatus>();

        // Query all known worker endpoints
        foreach (var endpoint in _options.WorkerEndpoints)
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                var response = await httpClient.GetAsync($"{endpoint}/api/worker/status");
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var status = JsonConvert.DeserializeObject<WorkerStatus>(json);
                    if (status != null)
                    {
                        statuses.Add(status);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get status from worker at {Endpoint}", endpoint);
            }
        }

        return statuses;
    }

    public async Task<bool> IsWorkerAvailableAsync()
    {
        var statuses = await GetWorkerStatusesAsync();
        return statuses.Any(s => s.State == WorkerState.Idle);
    }

    private async Task ProcessResultsAsync()
    {
        while (!_serviceBusClient.IsClosed)
        {
            try
            {
                var message = await _resultReceiver.ReceiveMessageAsync(TimeSpan.FromSeconds(30));
                if (message != null)
                {
                    var body = Encoding.UTF8.GetString(message.Body);
                    var result = JsonConvert.DeserializeObject<WorkerTaskResult>(body);
                    
                    if (result != null && _pendingResults.TryRemove(result.TaskId, out var tcs))
                    {
                        tcs.TrySetResult(result);
                        _logger.LogInformation("Received result for task {TaskId}", result.TaskId);
                    }

                    await _resultReceiver.CompleteMessageAsync(message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing result message");
            }
        }
    }

    public void Dispose()
    {
        _taskSender?.DisposeAsync().AsTask().Wait();
        _resultReceiver?.DisposeAsync().AsTask().Wait();
        _serviceBusClient?.DisposeAsync().AsTask().Wait();
    }
}