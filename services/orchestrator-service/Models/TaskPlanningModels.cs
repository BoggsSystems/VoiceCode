using System;
using System.Collections.Generic;
using System.Linq;

namespace VoiceCode.OrchestratorService.Models
{
    /// <summary>
    /// Represents a voice-optimized task plan that breaks down complex requests into phases
    /// </summary>
    public class VoiceTaskPlan
    {
        public string PlanId { get; set; } = Guid.NewGuid().ToString();
        public string SessionId { get; set; }
        public string OriginalPrompt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public List<TaskPhase> Phases { get; set; } = new();
        public PlanMetadata Metadata { get; set; } = new();
        public ExecutionStrategy Strategy { get; set; } = new();
        public PlanStatus Status { get; set; } = PlanStatus.Planning;
        public TimeSpan EstimatedDuration => TimeSpan.FromMinutes(Phases.Sum(p => p.EstimatedMinutes));
        public VoiceProgress Progress { get; set; } = new();
    }

    /// <summary>
    /// Represents a phase in a multi-phase project
    /// </summary>
    public class TaskPhase
    {
        public string PhaseId { get; set; } = Guid.NewGuid().ToString();
        public int PhaseNumber { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public PhaseType Type { get; set; }
        public List<WorkerTask> Tasks { get; set; } = new();
        public List<string> Dependencies { get; set; } = new(); // PhaseIds this depends on
        public Dictionary<string, object> Context { get; set; } = new();
        public int EstimatedMinutes { get; set; }
        public PhaseStatus Status { get; set; } = PhaseStatus.Pending;
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public List<PhaseArtifact> Artifacts { get; set; } = new();
        public WorkerSpecialization RequiredSpecialization { get; set; }
    }

    /// <summary>
    /// Types of phases in a project
    /// </summary>
    public enum PhaseType
    {
        Setup,
        Design,
        Implementation,
        Testing,
        Documentation,
        Review,
        Deployment,
        Optimization
    }

    /// <summary>
    /// Worker specialization requirements
    /// </summary>
    public enum WorkerSpecialization
    {
        General,
        Frontend,
        Backend,
        Database,
        Testing,
        DevOps,
        Security,
        Performance,
        Documentation
    }

    /// <summary>
    /// Status of a phase
    /// </summary>
    public enum PhaseStatus
    {
        Pending,
        Queued,
        InProgress,
        Completed,
        Failed,
        Blocked,
        Skipped
    }

    /// <summary>
    /// Overall plan status
    /// </summary>
    public enum PlanStatus
    {
        Planning,
        Executing,
        Paused,
        Completed,
        Failed,
        Cancelled
    }

    /// <summary>
    /// Artifacts produced by a phase
    /// </summary>
    public class PhaseArtifact
    {
        public string ArtifactId { get; set; } = Guid.NewGuid().ToString();
        public string Type { get; set; } // file, code, config, etc.
        public string Path { get; set; }
        public string Description { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Metadata about the plan
    /// </summary>
    public class PlanMetadata
    {
        public string ProjectType { get; set; } // web-app, api, cli-tool, etc.
        public List<string> Technologies { get; set; } = new();
        public string PrimaryLanguage { get; set; }
        public List<string> Frameworks { get; set; } = new();
        public ComplexityLevel Complexity { get; set; }
        public int EstimatedLinesOfCode { get; set; }
        public List<string> RequiredServices { get; set; } = new();
        public Dictionary<string, string> Constraints { get; set; } = new();
    }

    /// <summary>
    /// Execution strategy for the plan
    /// </summary>
    public class ExecutionStrategy
    {
        public ExecutionMode Mode { get; set; } = ExecutionMode.Sequential;
        public int MaxParallelPhases { get; set; } = 3;
        public bool AllowPhaseSkipping { get; set; } = false;
        public bool RequireConfirmationBetweenPhases { get; set; } = true;
        public TimeSpan PhaseTimeout { get; set; } = TimeSpan.FromMinutes(20);
        public RetryPolicy RetryPolicy { get; set; } = new();
    }

    /// <summary>
    /// Execution modes for phases
    /// </summary>
    public enum ExecutionMode
    {
        Sequential,      // One phase at a time
        Parallel,        // Multiple phases in parallel
        Adaptive,        // Mix based on dependencies
        Pipeline         // Streaming between phases
    }

    /// <summary>
    /// Complexity levels
    /// </summary>
    public enum ComplexityLevel
    {
        Simple,          // Single file, < 100 LOC
        Moderate,        // Multiple files, < 500 LOC
        Complex,         // Multiple components, < 2000 LOC
        VeryComplex      // Large system, > 2000 LOC
    }

    /// <summary>
    /// Retry policy for failed phases
    /// </summary>
    public class RetryPolicy
    {
        public int MaxRetries { get; set; } = 2;
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(30);
        public bool ExponentialBackoff { get; set; } = true;
        public List<PhaseType> RetryablePhases { get; set; } = new() 
        { 
            PhaseType.Implementation, 
            PhaseType.Testing 
        };
    }

    /// <summary>
    /// Voice-optimized progress reporting
    /// </summary>
    public class VoiceProgress
    {
        public int TotalPhases { get; set; }
        public int CompletedPhases { get; set; }
        public int FailedPhases { get; set; }
        public string CurrentPhase { get; set; }
        public double OverallPercentage => TotalPhases > 0 ? (double)CompletedPhases / TotalPhases * 100 : 0;
        public TimeSpan ElapsedTime { get; set; }
        public TimeSpan EstimatedTimeRemaining { get; set; }
        public List<string> KeyAchievements { get; set; } = new();
        public List<string> PendingActions { get; set; } = new();
        public string NextMilestone { get; set; }
        
        public string GetVoiceSummary()
        {
            if (TotalPhases == 0) return "No phases defined yet.";
            
            if (CompletedPhases == TotalPhases)
                return $"All {TotalPhases} phases completed successfully!";
            
            return $"Progress: {CompletedPhases} of {TotalPhases} phases complete. " +
                   $"Currently working on {CurrentPhase}. " +
                   $"About {EstimatedTimeRemaining.TotalMinutes:F0} minutes remaining.";
        }
    }

    /// <summary>
    /// Context sharing between workers
    /// </summary>
    public class SharedContext
    {
        public string ContextId { get; set; } = Guid.NewGuid().ToString();
        public string PlanId { get; set; }
        public Dictionary<string, object> GlobalVariables { get; set; } = new();
        public Dictionary<string, FileReference> SharedFiles { get; set; } = new();
        public Dictionary<string, CodeSnippet> SharedCode { get; set; } = new();
        public List<ContextUpdate> UpdateHistory { get; set; } = new();
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Reference to a shared file
    /// </summary>
    public class FileReference
    {
        public string FilePath { get; set; }
        public string Description { get; set; }
        public string CreatedByPhase { get; set; }
        public List<string> UsedByPhases { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public long Size { get; set; }
        public string Hash { get; set; }
    }

    /// <summary>
    /// Shared code snippet
    /// </summary>
    public class CodeSnippet
    {
        public string Name { get; set; }
        public string Language { get; set; }
        public string Code { get; set; }
        public string Purpose { get; set; }
        public List<string> Dependencies { get; set; } = new();
    }

    /// <summary>
    /// Context update history
    /// </summary>
    public class ContextUpdate
    {
        public DateTime Timestamp { get; set; }
        public string PhaseId { get; set; }
        public string WorkerId { get; set; }
        public string UpdateType { get; set; }
        public Dictionary<string, object> Changes { get; set; } = new();
    }

    /// <summary>
    /// Worker handoff information
    /// </summary>
    public class WorkerHandoff
    {
        public string HandoffId { get; set; } = Guid.NewGuid().ToString();
        public string FromWorkerId { get; set; }
        public string ToWorkerId { get; set; }
        public string PhaseId { get; set; }
        public DateTime HandoffTime { get; set; } = DateTime.UtcNow;
        public HandoffReason Reason { get; set; }
        public Dictionary<string, object> Context { get; set; } = new();
        public List<string> TransferredFiles { get; set; } = new();
        public string Summary { get; set; }
        public string NextSteps { get; set; }
    }

    /// <summary>
    /// Reasons for worker handoff
    /// </summary>
    public enum HandoffReason
    {
        PhaseCompletion,
        SpecializationRequired,
        LoadBalancing,
        WorkerFailure,
        TimeLimit,
        UserRequest
    }
}