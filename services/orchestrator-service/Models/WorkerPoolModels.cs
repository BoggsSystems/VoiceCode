using System;
using System.Collections.Generic;

namespace VoiceCode.OrchestratorService.Models
{
    public class WorkerRegistration
    {
        public string WorkerId { get; set; } = Guid.NewGuid().ToString();
        public string Endpoint { get; set; }
        public WorkerCapabilities Capabilities { get; set; } = new();
        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
        public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
        public WorkerHealthStatus HealthStatus { get; set; } = new();
        public WorkerMetrics Metrics { get; set; } = new();
        public int CurrentLoad { get; set; }
        public int MaxCapacity { get; set; } = 5;
        public bool IsAvailable => CurrentLoad < MaxCapacity && HealthStatus.IsHealthy;
    }

    public class WorkerCapabilities
    {
        public List<string> SupportedLanguages { get; set; } = new();
        public List<string> SupportedFrameworks { get; set; } = new();
        public List<string> SupportedTaskTypes { get; set; } = new();
        public bool SupportsParallelExecution { get; set; } = true;
        public bool SupportsLargeProjects { get; set; } = true;
        public int MaxConcurrentTasks { get; set; } = 5;
    }

    public class WorkerHealthStatus
    {
        public bool IsHealthy { get; set; } = true;
        public string Status { get; set; } = "healthy";
        public DateTime LastHealthCheck { get; set; } = DateTime.UtcNow;
        public int ConsecutiveFailures { get; set; }
        public string LastError { get; set; }
        public double CpuUsage { get; set; }
        public double MemoryUsage { get; set; }
        public int ActiveTasks { get; set; }
    }

    public class WorkerMetrics
    {
        public int TotalTasksCompleted { get; set; }
        public int TotalTasksFailed { get; set; }
        public double AverageTaskDuration { get; set; }
        public double SuccessRate => TotalTasksCompleted + TotalTasksFailed > 0 
            ? (double)TotalTasksCompleted / (TotalTasksCompleted + TotalTasksFailed) * 100 
            : 0;
        public DateTime LastTaskCompletedAt { get; set; }
        public Dictionary<string, int> TaskTypeBreakdown { get; set; } = new();
    }

    public class TaskAssignment
    {
        public string TaskId { get; set; }
        public string WorkerId { get; set; }
        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
        public string Reason { get; set; }
        public int Priority { get; set; }
    }

    public class LoadBalancingStrategy
    {
        public const string RoundRobin = "RoundRobin";
        public const string LeastLoaded = "LeastLoaded";
        public const string CapabilityBased = "CapabilityBased";
        public const string Performance = "Performance";
        public const string Adaptive = "Adaptive";
    }

    public class WorkerPoolStatus
    {
        public int TotalWorkers { get; set; }
        public int HealthyWorkers { get; set; }
        public int AvailableWorkers { get; set; }
        public int TotalCapacity { get; set; }
        public int CurrentLoad { get; set; }
        public double UtilizationPercentage => TotalCapacity > 0 
            ? (double)CurrentLoad / TotalCapacity * 100 
            : 0;
        public Dictionary<string, WorkerRegistration> Workers { get; set; } = new();
        public List<string> UnhealthyWorkers { get; set; } = new();
    }

    public class TaskDistributionPolicy
    {
        public string Strategy { get; set; } = LoadBalancingStrategy.LeastLoaded;
        public bool EnableAutoScaling { get; set; } = true;
        public int MinWorkers { get; set; } = 1;
        public int MaxWorkers { get; set; } = 10;
        public double ScaleUpThreshold { get; set; } = 80; // % utilization
        public double ScaleDownThreshold { get; set; } = 20; // % utilization
        public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);
        public int MaxRetries { get; set; } = 3;
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    }
}