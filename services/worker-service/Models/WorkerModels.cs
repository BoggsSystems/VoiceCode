namespace VoiceCode.WorkerService.Models;

public class WorkerTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Type { get; set; } = string.Empty; // "code_generation", "refactoring", "testing", etc.
    public string Description { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public WorkerTaskStatus Status { get; set; } = WorkerTaskStatus.Pending;
    public string? Error { get; set; }
}

public enum WorkerTaskStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Cancelled
}

public class WorkerTaskResult
{
    public string TaskId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public List<FileOperation> FileOperations { get; set; } = new();
    public List<CommandExecution> CommandExecutions { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
    public string? Error { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class FileOperation
{
    public string Type { get; set; } = string.Empty; // "create", "edit", "delete", "read"
    public string FilePath { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string? OldContent { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class CommandExecution
{
    public string Command { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public int ExitCode { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public TimeSpan Duration { get; set; }
}

public class WorkerStatus
{
    public string WorkerId { get; set; } = string.Empty;
    public WorkerState State { get; set; } = WorkerState.Idle;
    public string? CurrentTaskId { get; set; }
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
    public int CompletedTasks { get; set; }
    public int FailedTasks { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public enum WorkerState
{
    Idle,
    Busy,
    Offline,
    Error
}

public enum WorkerTaskType
{
    CodeGeneration,
    Implementation,
    Enhancement,
    Refactoring,
    Testing,
    Documentation,
    Debugging
}

public class ImplementationContext
{
    public BusinessAnalysis BusinessContext { get; set; }
    public ProductDesign ProductContext { get; set; }
    public TechnicalDesign TechnicalContext { get; set; }
    public ArchitectureDesign ArchitectureContext { get; set; }
    public List<ConversationTurn> ConversationHistory { get; set; } = new();
}

// Import shared models from orchestrator
public class BusinessAnalysis
{
    public string ProblemStatement { get; set; }
    public string MarketAnalysis { get; set; }
    public List<string> TargetUsers { get; set; } = new();
    public List<string> ValuePropositions { get; set; } = new();
    public List<string> Competitors { get; set; } = new();
    public List<string> Risks { get; set; } = new();
    public string RevenueModel { get; set; }
    public string FeasibilityScore { get; set; }
    public Dictionary<string, object> AdditionalInsights { get; set; } = new();
}

public class ProductDesign
{
    public string ProductVision { get; set; }
    public List<string> CoreFeatures { get; set; } = new();
    public List<string> FutureFeatures { get; set; } = new();
    public List<UserPersona> UserPersonas { get; set; } = new();
    public List<UserFlow> UserFlows { get; set; } = new();
    public string SuccessMetrics { get; set; }
}

public class UserPersona
{
    public string Name { get; set; }
    public string Description { get; set; }
    public List<string> Goals { get; set; } = new();
    public List<string> PainPoints { get; set; } = new();
}

public class UserFlow
{
    public string Name { get; set; }
    public string Description { get; set; }
    public List<string> Steps { get; set; } = new();
}

public class TechnicalDesign
{
    public List<string> TechnologyStack { get; set; } = new();
    public List<ApiEndpoint> ApiDesign { get; set; } = new();
    public List<DataModel> DataModels { get; set; } = new();
    public List<string> SecurityRequirements { get; set; } = new();
    public List<string> IntegrationPoints { get; set; } = new();
    public Dictionary<string, object> PerformanceRequirements { get; set; } = new();
}

public class ApiEndpoint
{
    public string Method { get; set; }
    public string Path { get; set; }
    public string Description { get; set; }
    public Dictionary<string, object> RequestSchema { get; set; }
    public Dictionary<string, object> ResponseSchema { get; set; }
}

public class DataModel
{
    public string Name { get; set; }
    public string Description { get; set; }
    public Dictionary<string, string> Fields { get; set; } = new();
    public List<string> Relationships { get; set; } = new();
}

public class ArchitectureDesign
{
    public string Pattern { get; set; }
    public List<string> Components { get; set; } = new();
    public string DeploymentStrategy { get; set; }
    public string ScalingStrategy { get; set; }
    public Dictionary<string, string> InfrastructureChoices { get; set; } = new();
    public List<string> NonFunctionalRequirements { get; set; } = new();
}

public class ConversationTurn
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Phase { get; set; }
    public string UserInput { get; set; }
    public string SystemResponse { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}