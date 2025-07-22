using VoiceCode.Common.Enums;

namespace VoiceCode.Common.Models;

public class WebSocketMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public WebSocketMessageType Type { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public object? Data { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class StatusUpdate
{
    public string RequestId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Progress { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

public class ResultNotification
{
    public string RequestId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string ResultUrl { get; set; } = string.Empty;
    public List<string> GeneratedFiles { get; set; } = new();
}