namespace VoiceCode.WorkerService.Models;

public class SidecarExecutionResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = string.Empty;
    public string? Error { get; set; }
    public string? SessionId { get; set; }
    public TimeSpan ExecutionTime { get; set; }
    public SidecarMetadata? Metadata { get; set; }
    public List<SidecarStreamUpdate>? StreamUpdates { get; set; }
}

public class SidecarMetadata
{
    public int TokensUsed { get; set; }
    public string Model { get; set; } = "claude-3-opus-20240229";
}

public class SidecarStreamUpdate
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Type { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public Dictionary<string, object> Data { get; set; } = new();
}