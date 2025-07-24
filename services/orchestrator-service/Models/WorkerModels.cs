namespace VoiceCode.OrchestratorService.Models;

public class WorkerTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Type { get; set; } = string.Empty;
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
    public string Type { get; set; } = string.Empty;
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