using Microsoft.Extensions.Diagnostics.HealthChecks;
using VoiceCode.OrchestratorService.Services;

namespace VoiceCode.OrchestratorService
{
    public class WorkerPoolHealthCheck : IHealthCheck
    {
        private readonly IWorkerPoolService _workerPool;
        private readonly ILogger<WorkerPoolHealthCheck> _logger;

        public WorkerPoolHealthCheck(IWorkerPoolService workerPool, ILogger<WorkerPoolHealthCheck> logger)
        {
            _workerPool = workerPool;
            _logger = logger;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, 
            CancellationToken cancellationToken = default)
        {
            try
            {
                var poolStatus = await _workerPool.GetPoolStatusAsync();
                var healthyWorkers = await _workerPool.GetHealthyWorkersAsync();

                var data = new Dictionary<string, object>
                {
                    ["total_workers"] = poolStatus.TotalWorkers,
                    ["healthy_workers"] = poolStatus.HealthyWorkers,
                    ["available_workers"] = poolStatus.AvailableWorkers,
                    ["utilization_percentage"] = poolStatus.UtilizationPercentage,
                    ["current_load"] = poolStatus.CurrentLoad,
                    ["total_capacity"] = poolStatus.TotalCapacity
                };

                if (poolStatus.TotalWorkers == 0)
                {
                    return HealthCheckResult.Unhealthy("No workers registered", null, data);
                }

                if (poolStatus.HealthyWorkers == 0)
                {
                    return HealthCheckResult.Unhealthy("No healthy workers available", null, data);
                }

                if (poolStatus.UtilizationPercentage > 90)
                {
                    return HealthCheckResult.Degraded("Worker pool utilization above 90%", null, data);
                }

                if (poolStatus.HealthyWorkers < poolStatus.TotalWorkers / 2)
                {
                    return HealthCheckResult.Degraded("More than half of workers are unhealthy", null, data);
                }

                return HealthCheckResult.Healthy("Worker pool is healthy", data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Worker pool health check failed");
                return HealthCheckResult.Unhealthy("Worker pool health check failed", ex);
            }
        }
    }
}