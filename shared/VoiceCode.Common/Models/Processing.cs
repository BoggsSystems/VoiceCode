using VoiceCode.Common.Enums;

namespace VoiceCode.Common.Models;

public class ProcessingRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public ProcessingType Type { get; set; }
    public object? Payload { get; set; }
    public ProcessingPriority Priority { get; set; } = ProcessingPriority.Normal;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> Metadata { get; set; } = new();
}

public class ProcessingResult
{
    public string Id { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public ProcessingStatus Status { get; set; }
    public string Summary { get; set; } = string.Empty;
    public object? Result { get; set; }
    public string AudioUrl { get; set; } = string.Empty;
    public List<FileChange> Files { get; set; } = new();
    public string TargetRepository { get; set; } = string.Empty;
    public string CommitMessage { get; set; } = string.Empty;
    public bool RequiresLocalExecution { get; set; }
    public bool SendNotification { get; set; }
    public DateTime ProcessedAt { get; set; }
    public ProcessingMetrics Metrics { get; set; } = new();
}

public class ProcessingMetrics
{
    public long ProcessingTimeMs { get; set; }
    public int TokensUsed { get; set; }
    public double EstimatedCost { get; set; }
    public Dictionary<string, long> StepDurations { get; set; } = new();
}