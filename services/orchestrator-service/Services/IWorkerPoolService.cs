using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VoiceCode.OrchestratorService.Models;

namespace VoiceCode.OrchestratorService.Services
{
    public interface IWorkerPoolService
    {
        // Worker Registration and Management
        Task<WorkerRegistration> RegisterWorkerAsync(string endpoint, WorkerCapabilities capabilities);
        Task<bool> UnregisterWorkerAsync(string workerId);
        Task<WorkerRegistration> GetWorkerAsync(string workerId);
        Task<List<WorkerRegistration>> GetAllWorkersAsync();
        Task<WorkerPoolStatus> GetPoolStatusAsync();

        // Health Monitoring
        Task<WorkerHealthStatus> CheckWorkerHealthAsync(string workerId);
        Task UpdateWorkerHealthAsync(string workerId, WorkerHealthStatus health);
        Task<List<WorkerRegistration>> GetHealthyWorkersAsync();
        Task<List<WorkerRegistration>> GetUnhealthyWorkersAsync();
        Task RestartUnhealthyWorkersAsync();

        // Task Assignment and Load Balancing
        Task<WorkerRegistration> SelectWorkerForTaskAsync(ClaudeCodeTask task);
        Task<TaskAssignment> AssignTaskToWorkerAsync(string taskId, string workerId, int priority = 0);
        Task ReleaseTaskFromWorkerAsync(string taskId, string workerId);
        Task<Dictionary<string, List<string>>> GetActiveTaskAssignmentsAsync();

        // Load Balancing Strategies
        Task<WorkerRegistration> ApplyLoadBalancingStrategyAsync(
            ClaudeCodeTask task, 
            string strategy = LoadBalancingStrategy.LeastLoaded);

        // Metrics and Monitoring
        Task UpdateWorkerMetricsAsync(string workerId, TaskResult result);
        Task<WorkerMetrics> GetWorkerMetricsAsync(string workerId);
        Task<Dictionary<string, WorkerMetrics>> GetAllWorkerMetricsAsync();

        // Auto-scaling
        Task<int> GetRecommendedWorkerCountAsync();
        Task ScaleWorkersAsync(int targetCount);
        Task AutoScaleAsync();

        // Configuration
        Task<TaskDistributionPolicy> GetDistributionPolicyAsync();
        Task UpdateDistributionPolicyAsync(TaskDistributionPolicy policy);
    }
}