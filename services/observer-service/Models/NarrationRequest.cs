namespace VoiceCode.ObserverService.Models;

public class NarrationRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string WorkerId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public NarrationPriority Priority { get; set; } = NarrationPriority.Normal;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public enum NarrationPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}