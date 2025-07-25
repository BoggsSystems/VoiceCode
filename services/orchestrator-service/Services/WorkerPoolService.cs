using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VoiceCode.OrchestratorService.Configuration;
using VoiceCode.OrchestratorService.Models;

namespace VoiceCode.OrchestratorService.Services
{
    public class WorkerPoolService : IWorkerPoolService, IHostedService
    {
        private readonly ILogger<WorkerPoolService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly OrchestrationOptions _options;
        private readonly ConcurrentDictionary<string, WorkerRegistration> _workers;
        private readonly ConcurrentDictionary<string, List<string>> _taskAssignments;
        private TaskDistributionPolicy _distributionPolicy;
        private Timer _healthCheckTimer;
        private Timer _autoScaleTimer;
        private int _roundRobinIndex = 0;

        public WorkerPoolService(
            ILogger<WorkerPoolService> logger,
            IHttpClientFactory httpClientFactory,
            IOptions<OrchestrationOptions> options)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _options = options.Value;
            _workers = new ConcurrentDictionary<string, WorkerRegistration>();
            _taskAssignments = new ConcurrentDictionary<string, List<string>>();
            _distributionPolicy = new TaskDistributionPolicy();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting Worker Pool Service");

            // Initialize workers from configuration
            InitializeWorkersFromConfiguration();

            // Start health check timer
            _healthCheckTimer = new Timer(
                async _ => await PerformHealthChecksAsync(),
                null,
                TimeSpan.Zero,
                _distributionPolicy.HealthCheckInterval);

            // Start auto-scale timer
            if (_distributionPolicy.EnableAutoScaling)
            {
                _autoScaleTimer = new Timer(
                    async _ => await AutoScaleAsync(),
                    null,
                    TimeSpan.FromMinutes(1),
                    TimeSpan.FromMinutes(1));
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping Worker Pool Service");
            _healthCheckTimer?.Dispose();
            _autoScaleTimer?.Dispose();
            return Task.CompletedTask;
        }

        private void InitializeWorkersFromConfiguration()
        {
            foreach (var endpoint in _options.WorkerEndpoints)
            {
                var capabilities = new WorkerCapabilities
                {
                    SupportedLanguages = new List<string> { "csharp", "python", "javascript", "typescript", "java", "go" },
                    SupportedFrameworks = new List<string> { "aspnetcore", "flask", "express", "react", "angular" },
                    SupportedTaskTypes = new List<string> { "code_generation", "refactoring", "testing", "debugging", "documentation" },
                    SupportsParallelExecution = true,
                    MaxConcurrentTasks = 5
                };

                RegisterWorkerAsync(endpoint, capabilities).Wait();
            }
        }

        public Task<WorkerRegistration> RegisterWorkerAsync(string endpoint, WorkerCapabilities capabilities)
        {
            var worker = new WorkerRegistration
            {
                Endpoint = endpoint,
                Capabilities = capabilities,
                MaxCapacity = capabilities.MaxConcurrentTasks
            };

            _workers.TryAdd(worker.WorkerId, worker);
            _taskAssignments.TryAdd(worker.WorkerId, new List<string>());

            _logger.LogInformation($"Registered worker {worker.WorkerId} at {endpoint}");
            return Task.FromResult(worker);
        }

        public Task<bool> UnregisterWorkerAsync(string workerId)
        {
            if (_workers.TryRemove(workerId, out var worker))
            {
                _taskAssignments.TryRemove(workerId, out _);
                _logger.LogInformation($"Unregistered worker {workerId}");
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<WorkerRegistration> GetWorkerAsync(string workerId)
        {
            _workers.TryGetValue(workerId, out var worker);
            return Task.FromResult(worker);
        }

        public Task<List<WorkerRegistration>> GetAllWorkersAsync()
        {
            return Task.FromResult(_workers.Values.ToList());
        }

        public Task<WorkerPoolStatus> GetPoolStatusAsync()
        {
            var workers = _workers.Values.ToList();
            var status = new WorkerPoolStatus
            {
                TotalWorkers = workers.Count,
                HealthyWorkers = workers.Count(w => w.HealthStatus.IsHealthy),
                AvailableWorkers = workers.Count(w => w.IsAvailable),
                TotalCapacity = workers.Sum(w => w.MaxCapacity),
                CurrentLoad = workers.Sum(w => w.CurrentLoad),
                Workers = new Dictionary<string, WorkerRegistration>(_workers),
                UnhealthyWorkers = workers.Where(w => !w.HealthStatus.IsHealthy).Select(w => w.WorkerId).ToList()
            };

            return Task.FromResult(status);
        }

        public async Task<WorkerHealthStatus> CheckWorkerHealthAsync(string workerId)
        {
            if (!_workers.TryGetValue(workerId, out var worker))
            {
                return new WorkerHealthStatus { IsHealthy = false, Status = "not_found" };
            }

            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5);

                var response = await httpClient.GetAsync($"{worker.Endpoint}/health");
                
                if (response.IsSuccessStatusCode)
                {
                    // Get worker status for active task count
                    var statusResponse = await httpClient.GetAsync($"{worker.Endpoint}/api/worker/status");
                    if (statusResponse.IsSuccessStatusCode)
                    {
                        var json = await statusResponse.Content.ReadAsStringAsync();
                        var workerStatus = System.Text.Json.JsonSerializer.Deserialize<WorkerStatus>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        worker.CurrentLoad = workerStatus?.CurrentTaskId != null ? 1 : 0;
                    }

                    return new WorkerHealthStatus
                    {
                        IsHealthy = true,
                        Status = "healthy",
                        LastHealthCheck = DateTime.UtcNow,
                        ConsecutiveFailures = 0,
                        ActiveTasks = worker.CurrentLoad
                    };
                }
                else
                {
                    return new WorkerHealthStatus
                    {
                        IsHealthy = false,
                        Status = $"unhealthy_http_{(int)response.StatusCode}",
                        LastHealthCheck = DateTime.UtcNow,
                        ConsecutiveFailures = worker.HealthStatus.ConsecutiveFailures + 1,
                        LastError = $"HTTP {response.StatusCode}"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Health check failed for worker {workerId}");
                return new WorkerHealthStatus
                {
                    IsHealthy = false,
                    Status = "unhealthy_error",
                    LastHealthCheck = DateTime.UtcNow,
                    ConsecutiveFailures = worker.HealthStatus.ConsecutiveFailures + 1,
                    LastError = ex.Message
                };
            }
        }

        public Task UpdateWorkerHealthAsync(string workerId, WorkerHealthStatus health)
        {
            if (_workers.TryGetValue(workerId, out var worker))
            {
                worker.HealthStatus = health;
                worker.LastHeartbeat = DateTime.UtcNow;
            }
            return Task.CompletedTask;
        }

        public Task<List<WorkerRegistration>> GetHealthyWorkersAsync()
        {
            var healthyWorkers = _workers.Values
                .Where(w => w.HealthStatus.IsHealthy)
                .ToList();
            return Task.FromResult(healthyWorkers);
        }

        public Task<List<WorkerRegistration>> GetUnhealthyWorkersAsync()
        {
            var unhealthyWorkers = _workers.Values
                .Where(w => !w.HealthStatus.IsHealthy)
                .ToList();
            return Task.FromResult(unhealthyWorkers);
        }

        public async Task RestartUnhealthyWorkersAsync()
        {
            var unhealthyWorkers = await GetUnhealthyWorkersAsync();
            foreach (var worker in unhealthyWorkers)
            {
                _logger.LogWarning($"Attempting to restart unhealthy worker {worker.WorkerId}");
                // In a real implementation, this would trigger container restart
                // For now, we'll just reset the health status
                worker.HealthStatus = new WorkerHealthStatus();
                worker.CurrentLoad = 0;
            }
        }

        public async Task<WorkerRegistration> SelectWorkerForTaskAsync(ClaudeCodeTask task)
        {
            return await ApplyLoadBalancingStrategyAsync(task, _distributionPolicy.Strategy);
        }

        public async Task<TaskAssignment> AssignTaskToWorkerAsync(string taskId, string workerId, int priority = 0)
        {
            if (!_workers.TryGetValue(workerId, out var worker))
            {
                throw new InvalidOperationException($"Worker {workerId} not found");
            }

            if (!worker.IsAvailable)
            {
                throw new InvalidOperationException($"Worker {workerId} is not available");
            }

            // Update task assignments
            if (_taskAssignments.TryGetValue(workerId, out var tasks))
            {
                tasks.Add(taskId);
            }

            // Update worker load
            worker.CurrentLoad++;

            var assignment = new TaskAssignment
            {
                TaskId = taskId,
                WorkerId = workerId,
                Priority = priority,
                Reason = $"Assigned using {_distributionPolicy.Strategy} strategy"
            };

            _logger.LogInformation($"Assigned task {taskId} to worker {workerId}");
            return assignment;
        }

        public Task ReleaseTaskFromWorkerAsync(string taskId, string workerId)
        {
            if (_workers.TryGetValue(workerId, out var worker))
            {
                worker.CurrentLoad = Math.Max(0, worker.CurrentLoad - 1);
            }

            if (_taskAssignments.TryGetValue(workerId, out var tasks))
            {
                tasks.Remove(taskId);
            }

            _logger.LogInformation($"Released task {taskId} from worker {workerId}");
            return Task.CompletedTask;
        }

        public Task<Dictionary<string, List<string>>> GetActiveTaskAssignmentsAsync()
        {
            return Task.FromResult(new Dictionary<string, List<string>>(_taskAssignments));
        }

        public async Task<WorkerRegistration> ApplyLoadBalancingStrategyAsync(ClaudeCodeTask task, string strategy)
        {
            var availableWorkers = await GetHealthyWorkersAsync();
            availableWorkers = availableWorkers.Where(w => w.IsAvailable).ToList();

            if (!availableWorkers.Any())
            {
                _logger.LogWarning("No available workers found");
                return null;
            }

            WorkerRegistration selectedWorker = null;

            switch (strategy)
            {
                case LoadBalancingStrategy.RoundRobin:
                    selectedWorker = availableWorkers[_roundRobinIndex % availableWorkers.Count];
                    _roundRobinIndex++;
                    break;

                case LoadBalancingStrategy.LeastLoaded:
                    selectedWorker = availableWorkers.OrderBy(w => w.CurrentLoad).First();
                    break;

                case LoadBalancingStrategy.CapabilityBased:
                    selectedWorker = SelectByCapability(availableWorkers, task);
                    break;

                case LoadBalancingStrategy.Performance:
                    selectedWorker = availableWorkers
                        .OrderByDescending(w => w.Metrics.SuccessRate)
                        .ThenBy(w => w.Metrics.AverageTaskDuration)
                        .First();
                    break;

                case LoadBalancingStrategy.Adaptive:
                    // Adaptive strategy combines multiple factors
                    selectedWorker = availableWorkers
                        .OrderBy(w => CalculateAdaptiveScore(w, task))
                        .First();
                    break;

                default:
                    selectedWorker = availableWorkers.First();
                    break;
            }

            _logger.LogInformation($"Selected worker {selectedWorker?.WorkerId} using {strategy} strategy");
            return selectedWorker;
        }

        private WorkerRegistration SelectByCapability(List<WorkerRegistration> workers, ClaudeCodeTask task)
        {
            // Score workers based on capability match
            var scoredWorkers = workers.Select(w => new
            {
                Worker = w,
                Score = CalculateCapabilityScore(w, task)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Worker.CurrentLoad);

            return scoredWorkers.FirstOrDefault()?.Worker ?? workers.First();
        }

        private int CalculateCapabilityScore(WorkerRegistration worker, ClaudeCodeTask task)
        {
            int score = 0;

            // Check language support
            if (task.Context.ContainsKey("language") && 
                worker.Capabilities.SupportedLanguages.Contains(task.Context["language"]))
            {
                score += 10;
            }

            // Check framework support
            if (task.Context.ContainsKey("framework") && 
                worker.Capabilities.SupportedFrameworks.Contains(task.Context["framework"]))
            {
                score += 5;
            }

            // Check task type support
            if (worker.Capabilities.SupportedTaskTypes.Contains(task.Type))
            {
                score += 10;
            }

            return score;
        }

        private double CalculateAdaptiveScore(WorkerRegistration worker, ClaudeCodeTask task)
        {
            double score = 0;

            // Load factor (lower is better)
            score += (double)worker.CurrentLoad / worker.MaxCapacity * 100;

            // Performance factor (higher is better)
            score -= worker.Metrics.SuccessRate;

            // Capability match (lower is better)
            score -= CalculateCapabilityScore(worker, task) * 2;

            // Recent activity (prefer recently active workers)
            var idleTime = DateTime.UtcNow - worker.Metrics.LastTaskCompletedAt;
            if (idleTime.TotalMinutes > 5)
            {
                score += idleTime.TotalMinutes;
            }

            return score;
        }

        public Task UpdateWorkerMetricsAsync(string workerId, TaskResult result)
        {
            if (_workers.TryGetValue(workerId, out var worker))
            {
                var metrics = worker.Metrics;
                
                if (result.Success)
                {
                    metrics.TotalTasksCompleted++;
                }
                else
                {
                    metrics.TotalTasksFailed++;
                }

                // Update average duration
                var totalTasks = metrics.TotalTasksCompleted + metrics.TotalTasksFailed;
                metrics.AverageTaskDuration = 
                    (metrics.AverageTaskDuration * (totalTasks - 1) + result.Duration.TotalSeconds) / totalTasks;

                metrics.LastTaskCompletedAt = DateTime.UtcNow;

                // Update task type breakdown
                if (metrics.TaskTypeBreakdown.ContainsKey(result.TaskType))
                {
                    metrics.TaskTypeBreakdown[result.TaskType]++;
                }
                else
                {
                    metrics.TaskTypeBreakdown[result.TaskType] = 1;
                }
            }

            return Task.CompletedTask;
        }

        public Task<WorkerMetrics> GetWorkerMetricsAsync(string workerId)
        {
            _workers.TryGetValue(workerId, out var worker);
            return Task.FromResult(worker?.Metrics);
        }

        public Task<Dictionary<string, WorkerMetrics>> GetAllWorkerMetricsAsync()
        {
            var metrics = _workers.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Metrics);
            return Task.FromResult(metrics);
        }

        public async Task<int> GetRecommendedWorkerCountAsync()
        {
            var poolStatus = await GetPoolStatusAsync();
            
            if (poolStatus.UtilizationPercentage > _distributionPolicy.ScaleUpThreshold)
            {
                // Scale up
                return Math.Min(poolStatus.TotalWorkers + 1, _distributionPolicy.MaxWorkers);
            }
            else if (poolStatus.UtilizationPercentage < _distributionPolicy.ScaleDownThreshold)
            {
                // Scale down
                return Math.Max(poolStatus.TotalWorkers - 1, _distributionPolicy.MinWorkers);
            }

            return poolStatus.TotalWorkers;
        }

        public async Task ScaleWorkersAsync(int targetCount)
        {
            var currentCount = _workers.Count;
            
            if (targetCount > currentCount)
            {
                // Scale up - in production, this would create new container instances
                _logger.LogInformation($"Scaling up from {currentCount} to {targetCount} workers");
            }
            else if (targetCount < currentCount)
            {
                // Scale down - remove idle workers first
                var idleWorkers = _workers.Values
                    .Where(w => w.CurrentLoad == 0)
                    .OrderBy(w => w.Metrics.LastTaskCompletedAt)
                    .Take(currentCount - targetCount)
                    .ToList();

                foreach (var worker in idleWorkers)
                {
                    await UnregisterWorkerAsync(worker.WorkerId);
                }
                
                _logger.LogInformation($"Scaled down from {currentCount} to {targetCount} workers");
            }
        }

        public async Task AutoScaleAsync()
        {
            if (!_distributionPolicy.EnableAutoScaling)
                return;

            try
            {
                var recommendedCount = await GetRecommendedWorkerCountAsync();
                await ScaleWorkersAsync(recommendedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto-scaling failed");
            }
        }

        public Task<TaskDistributionPolicy> GetDistributionPolicyAsync()
        {
            return Task.FromResult(_distributionPolicy);
        }

        public Task UpdateDistributionPolicyAsync(TaskDistributionPolicy policy)
        {
            _distributionPolicy = policy;
            
            // Restart timers with new intervals if needed
            if (_healthCheckTimer != null)
            {
                _healthCheckTimer.Change(TimeSpan.Zero, policy.HealthCheckInterval);
            }

            return Task.CompletedTask;
        }

        private async Task PerformHealthChecksAsync()
        {
            var tasks = _workers.Values.Select(async worker =>
            {
                var health = await CheckWorkerHealthAsync(worker.WorkerId);
                await UpdateWorkerHealthAsync(worker.WorkerId, health);
            });

            await Task.WhenAll(tasks);

            // Restart unhealthy workers if they've failed too many times
            var unhealthyWorkers = await GetUnhealthyWorkersAsync();
            var workersToRestart = unhealthyWorkers
                .Where(w => w.HealthStatus.ConsecutiveFailures >= 3)
                .ToList();

            if (workersToRestart.Any())
            {
                _logger.LogWarning($"Found {workersToRestart.Count} workers to restart");
                await RestartUnhealthyWorkersAsync();
            }
        }
    }
}