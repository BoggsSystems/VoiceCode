using VoiceCode.Common.Enums;

namespace VoiceCode.Common.Models;

public class ProcessingResult
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RequestId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public ProcessingStatus Status { get; set; }
    public object? Result { get; set; }
    public string? Error { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public List<ProcessingStep> Steps { get; set; } = new();
}

public class ProcessingStep
{
    public string Name { get; set; } = string.Empty;
    public ProcessingStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Error { get; set; }
    public Dictionary<string, object> Data { get; set; } = new();
}

public class DispatchResult
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ProcessingResultId { get; set; } = string.Empty;
    public List<DispatchTarget> Targets { get; set; } = new();
    public DispatchStatus Status { get; set; }
    public DateTime DispatchedAt { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class DispatchTarget
{
    public string Type { get; set; } = string.Empty; // "websocket", "webhook", "storage", etc.
    public string Destination { get; set; } = string.Empty;
    public DispatchStatus Status { get; set; }
    public string? Error { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public int RetryCount { get; set; }
}

public enum DispatchStatus
{
    Pending,
    Sent,
    Delivered,
    Failed,
    Retrying
}

public class UserSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string SessionToken { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty; // SignalR connection ID
    public DateTime CreatedAt { get; set; }
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; set; }
    public DateTime LastActivity { get; set; } = DateTime.UtcNow; // Alias for LastActivityAt
    public DateTime? EndTime { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public SessionState State { get; set; }
    public UserContext Context { get; set; } = new();
    public int MessageCount { get; set; } = 0;
    public Dictionary<string, object> Data { get; set; } = new();
    public List<string> ActiveConnections { get; set; } = new();
}

public enum SessionState
{
    Active,
    Idle,
    Suspended,
    Expired,
    Terminated,
    Ended
}

