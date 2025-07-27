namespace VoiceCode.Common.Models;

public class AudioResponseMessage
{
    public string SessionId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string AudioUrl { get; set; } = string.Empty;
    public string TranscriptionText { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object>? Metadata { get; set; }
}

public class AudioResponseRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string AudioBlobPath { get; set; } = string.Empty;
    public string TranscriptionText { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
}