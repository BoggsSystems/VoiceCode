using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using VoiceCode.OrchestratorService.Configuration;
using VoiceCode.OrchestratorService.Models;

namespace VoiceCode.OrchestratorService.Services
{
    public class EnhancedWorkerManagementService : IWorkerManagementService, IDisposable
    {
        private readonly ILogger<EnhancedWorkerManagementService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IWorkerPoolService _workerPool;
        private readonly OrchestrationOptions _options;
        private readonly ServiceBusClient _serviceBusClient;
        private readonly ServiceBusSender _taskSender;
        private readonly ServiceBusReceiver _resultReceiver;
        private readonly ConcurrentDictionary<string, TaskExecutionContext> _activeTasks;
        private readonly SemaphoreSlim _parallelExecutionSemaphore;
        private readonly Timer _taskMonitorTimer;

        private class TaskExecutionContext
        {
            public string TaskId { get; set; }
            public string WorkerId { get; set; }
            public WorkerTask Task { get; set; }
            public TaskCompletionSource<WorkerTaskResult> CompletionSource { get; set; }
            public DateTime StartTime { get; set; }
            public int RetryCount { get; set; }
            public CancellationTokenSource CancellationTokenSource { get; set; }
        }

        public EnhancedWorkerManagementService(
            ILogger<EnhancedWorkerManagementService> logger,
            IHttpClientFactory httpClientFactory,
            IWorkerPoolService workerPool,
            IOptions<OrchestrationOptions> options)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _workerPool = workerPool;
            _options = options.Value;
            _activeTasks = new ConcurrentDictionary<string, TaskExecutionContext>();
            _parallelExecutionSemaphore = new SemaphoreSlim(_options.MaxConcurrentWorkers ?? 10);

            // Initialize Service Bus
            var connectionString = Environment.GetEnvironmentVariable("AZURE_SERVICE_BUS_CONNECTION_STRING");
            if (!string.IsNullOrEmpty(connectionString))
            {
                _serviceBusClient = new ServiceBusClient(connectionString);
                _taskSender = _serviceBusClient.CreateSender("claude-code-tasks");
                _resultReceiver = _serviceBusClient.CreateReceiver("worker-results");
                
                // Start listening for results
                _ = Task.Run(ProcessResultsAsync);
            }
            else
            {
                _logger.LogWarning("Service Bus connection string not configured");
            }

            // Start task monitoring
            _taskMonitorTimer = new Timer(
                MonitorActiveTasks,
                null,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30));
        }

        public async Task<string> SubmitTaskAsync(WorkerTask task)
        {
            await _parallelExecutionSemaphore.WaitAsync();
            
            try
            {
                _logger.LogInformation("Submitting task {TaskId} of type {TaskType}", task.Id, task.Type);

                // Convert WorkerTask to ClaudeCodeTask for worker selection
                var claudeTask = new ClaudeCodeTask
                {
                    Id = task.Id,
                    Type = task.Type,
                    Prompt = task.Input,
                    Context = task.Context ?? new Dictionary<string, string>()
                };

                // Select a worker using the pool service
                var selectedWorker = await _workerPool.SelectWorkerForTaskAsync(claudeTask);
                if (selectedWorker == null)
                {
                    throw new InvalidOperationException("No available workers found");
                }

                // Create execution context
                var context = new TaskExecutionContext
                {
                    TaskId = task.Id,
                    WorkerId = selectedWorker.WorkerId,
                    Task = task,
                    CompletionSource = new TaskCompletionSource<WorkerTaskResult>(),
                    StartTime = DateTime.UtcNow,
                    RetryCount = 0,
                    CancellationTokenSource = new CancellationTokenSource()
                };

                _activeTasks[task.Id] = context;

                // Assign task to worker
                await _workerPool.AssignTaskToWorkerAsync(task.Id, selectedWorker.WorkerId);

                // Send task to queue with worker assignment
                await SendTaskToQueueAsync(task, selectedWorker.WorkerId);

                _logger.LogInformation("Task {TaskId} submitted to worker {WorkerId}", task.Id, selectedWorker.WorkerId);

                return task.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to submit task {TaskId}", task.Id);
                _parallelExecutionSemaphore.Release();
                throw;
            }
        }

        private async Task SendTaskToQueueAsync(WorkerTask task, string workerId)
        {
            var enhancedTask = new
            {
                task.Id,
                task.Type,
                task.Input,
                task.Context,
                WorkerId = workerId,
                SubmittedAt = DateTime.UtcNow
            };

            var messageBody = JsonConvert.SerializeObject(enhancedTask);
            var message = new ServiceBusMessage(Encoding.UTF8.GetBytes(messageBody))
            {
                ContentType = "application/json",
                Subject = task.Type,
                MessageId = task.Id,
                TimeToLive = TimeSpan.FromMinutes(_options.TaskTimeoutMinutes),
                ApplicationProperties =
                {
                    ["WorkerId"] = workerId,
                    ["TaskType"] = task.Type,
                    ["Priority"] = task.Context?.GetValueOrDefault("priority", "normal") ?? "normal"
                }
            };

            await _taskSender.SendMessageAsync(message);
        }

        public async Task<WorkerTaskResult?> GetTaskResultAsync(string taskId, TimeSpan timeout)
        {
            if (_activeTasks.TryGetValue(taskId, out var context))
            {
                using (context.CancellationTokenSource.Token.Register(
                    () => context.CompletionSource.TrySetCanceled()))
                {
                    try
                    {
                        var timeoutTask = Task.Delay(timeout, context.CancellationTokenSource.Token);
                        var resultTask = context.CompletionSource.Task;

                        var completedTask = await Task.WhenAny(resultTask, timeoutTask);
                        
                        if (completedTask == resultTask)
                        {
                            return await resultTask;
                        }
                        else
                        {
                            _logger.LogWarning("Timeout waiting for task {TaskId} result", taskId);
                            await HandleTaskTimeoutAsync(context);
                            return null;
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        _logger.LogWarning("Task {TaskId} was cancelled", taskId);
                        return null;
                    }
                    finally
                    {
                        await CleanupTaskAsync(taskId);
                    }
                }
            }

            _logger.LogWarning("No active task found for {TaskId}", taskId);
            return null;
        }

        public async Task<List<WorkerStatus>> GetWorkerStatusesAsync()
        {
            var statuses = new List<WorkerStatus>();
            var workers = await _workerPool.GetAllWorkersAsync();

            foreach (var worker in workers)
            {
                statuses.Add(new WorkerStatus
                {
                    WorkerId = worker.WorkerId,
                    State = worker.IsAvailable ? WorkerState.Idle : WorkerState.Busy,
                    ActiveWorkers = worker.CurrentLoad,
                    MaxWorkers = worker.MaxCapacity,
                    LastHeartbeat = worker.LastHeartbeat,
                    Health = worker.HealthStatus.IsHealthy ? "healthy" : "unhealthy"
                });
            }

            return statuses;
        }

        public async Task<bool> IsWorkerAvailableAsync()
        {
            var poolStatus = await _workerPool.GetPoolStatusAsync();
            return poolStatus.AvailableWorkers > 0;
        }

        private async Task ProcessResultsAsync()
        {
            while (!_serviceBusClient.IsClosed)
            {
                try
                {
                    var messages = await _resultReceiver.ReceiveMessagesAsync(10, TimeSpan.FromSeconds(5));
                    
                    foreach (var message in messages)
                    {
                        try
                        {
                            await ProcessResultMessageAsync(message);
                            await _resultReceiver.CompleteMessageAsync(message);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing result message {MessageId}", message.MessageId);
                            await _resultReceiver.AbandonMessageAsync(message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in result processing loop");
                    await Task.Delay(TimeSpan.FromSeconds(5));
                }
            }
        }

        private async Task ProcessResultMessageAsync(ServiceBusReceivedMessage message)
        {
            var body = Encoding.UTF8.GetString(message.Body);
            var result = JsonConvert.DeserializeObject<WorkerTaskResult>(body);
            
            if (result != null && _activeTasks.TryGetValue(result.TaskId, out var context))
            {
                // Update worker metrics
                var taskResult = new TaskResult
                {
                    TaskId = result.TaskId,
                    Success = result.Success,
                    Duration = DateTime.UtcNow - context.StartTime,
                    TaskType = context.Task.Type
                };

                await _workerPool.UpdateWorkerMetricsAsync(context.WorkerId, taskResult);

                // Complete the task
                context.CompletionSource.TrySetResult(result);
                _logger.LogInformation("Received result for task {TaskId} from worker {WorkerId}", 
                    result.TaskId, context.WorkerId);
            }
        }

        private void MonitorActiveTasks(object state)
        {
            var now = DateTime.UtcNow;
            var timeoutTasks = _activeTasks.Values
                .Where(t => now - t.StartTime > TimeSpan.FromMinutes(_options.TaskTimeoutMinutes))
                .ToList();

            foreach (var task in timeoutTasks)
            {
                _logger.LogWarning("Task {TaskId} has timed out after {Minutes} minutes", 
                    task.TaskId, (now - task.StartTime).TotalMinutes);
                
                _ = HandleTaskTimeoutAsync(task);
            }
        }

        private async Task HandleTaskTimeoutAsync(TaskExecutionContext context)
        {
            var policy = await _workerPool.GetDistributionPolicyAsync();
            
            if (context.RetryCount < policy.MaxRetries)
            {
                _logger.LogInformation("Retrying task {TaskId} (attempt {Attempt})", 
                    context.TaskId, context.RetryCount + 1);
                
                context.RetryCount++;
                context.StartTime = DateTime.UtcNow;
                
                // Release from current worker
                await _workerPool.ReleaseTaskFromWorkerAsync(context.TaskId, context.WorkerId);
                
                // Select a new worker (possibly different one)
                var claudeTask = new ClaudeCodeTask
                {
                    Id = context.Task.Id,
                    Type = context.Task.Type,
                    Prompt = context.Task.Input,
                    Context = context.Task.Context ?? new Dictionary<string, string>()
                };
                
                var newWorker = await _workerPool.SelectWorkerForTaskAsync(claudeTask);
                if (newWorker != null && newWorker.WorkerId != context.WorkerId)
                {
                    context.WorkerId = newWorker.WorkerId;
                    await _workerPool.AssignTaskToWorkerAsync(context.TaskId, newWorker.WorkerId);
                    await SendTaskToQueueAsync(context.Task, newWorker.WorkerId);
                }
                else
                {
                    // No alternative worker available
                    context.CompletionSource.TrySetException(
                        new TimeoutException($"Task {context.TaskId} timed out and no alternative workers available"));
                }
            }
            else
            {
                // Max retries exceeded
                context.CompletionSource.TrySetException(
                    new TimeoutException($"Task {context.TaskId} exceeded maximum retries"));
            }
        }

        private async Task CleanupTaskAsync(string taskId)
        {
            if (_activeTasks.TryRemove(taskId, out var context))
            {
                await _workerPool.ReleaseTaskFromWorkerAsync(taskId, context.WorkerId);
                context.CancellationTokenSource?.Dispose();
                _parallelExecutionSemaphore.Release();
            }
        }

        public void Dispose()
        {
            _taskMonitorTimer?.Dispose();
            _parallelExecutionSemaphore?.Dispose();
            
            // Cancel all active tasks
            foreach (var context in _activeTasks.Values)
            {
                context.CancellationTokenSource?.Cancel();
                context.CancellationTokenSource?.Dispose();
            }
            
            _taskSender?.DisposeAsync().AsTask().Wait();
            _resultReceiver?.DisposeAsync().AsTask().Wait();
            _serviceBusClient?.DisposeAsync().AsTask().Wait();
        }
    }
}