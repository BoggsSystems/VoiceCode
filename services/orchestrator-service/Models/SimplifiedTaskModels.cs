using System;
using System.Collections.Generic;

namespace VoiceCode.OrchestratorService.Models
{
    /// <summary>
    /// Simplified voice task that delegates planning to Claude Code
    /// </summary>
    public class VoiceTask
    {
        public string TaskId { get; set; } = Guid.NewGuid().ToString();
        public string SessionId { get; set; }
        public string VoicePrompt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public TaskStatus Status { get; set; } = TaskStatus.Pending;
        public string AssignedWorkerId { get; set; }
        public VoiceTaskResult Result { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Simplified task status
    /// </summary>
    public enum TaskStatus
    {
        Pending,
        Assigned,
        InProgress,
        Completed,
        Failed,
        Cancelled
    }

    /// <summary>
    /// Result from Claude Code execution
    /// </summary>
    public class VoiceTaskResult
    {
        public bool Success { get; set; }
        public string Summary { get; set; }
        public List<string> FilesCreated { get; set; } = new();
        public List<string> FilesModified { get; set; } = new();
        public Dictionary<string, string> KeyOutcomes { get; set; } = new();
        public string VoiceFriendlyResponse { get; set; }
        public TimeSpan Duration { get; set; }
        public string Error { get; set; }
    }

    /// <summary>
    /// Progress update from Claude Code
    /// </summary>
    public class TaskProgress
    {
        public string TaskId { get; set; }
        public string CurrentActivity { get; set; }
        public double PercentComplete { get; set; }
        public string EstimatedTimeRemaining { get; set; }
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Worker specialization (simplified)
    /// </summary>
    public class WorkerProfile
    {
        public string WorkerId { get; set; }
        public List<string> PreferredLanguages { get; set; } = new();
        public List<string> PreferredTaskTypes { get; set; } = new();
        public int CompletedTasks { get; set; }
        public double SuccessRate { get; set; }
        public double AverageTaskDuration { get; set; }
    }

    /// <summary>
    /// Simple context for voice interactions
    /// </summary>
    public class VoiceContext
    {
        public string SessionId { get; set; }
        public string UserId { get; set; }
        public Dictionary<string, string> UserPreferences { get; set; } = new();
        public List<string> RecentTasks { get; set; } = new();
        public string CurrentProject { get; set; }
    }
}