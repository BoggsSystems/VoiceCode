using Azure;
using Azure.Data.Tables;

namespace VoiceCode.Common.Models;

public class AudioResponseEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "AudioResponses";
    public string RowKey { get; set; } = string.Empty; // Will be TaskId
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    
    // Audio response properties
    public string TaskId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string AudioUrl { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public double Duration { get; set; }
    public string Status { get; set; } = "ready";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public AudioResponseEntity() { }
    
    public AudioResponseEntity(string taskId, string sessionId, string audioUrl, string text, double duration)
    {
        PartitionKey = "AudioResponses";
        RowKey = taskId;
        TaskId = taskId;
        SessionId = sessionId;
        AudioUrl = audioUrl;
        Text = text;
        Duration = duration;
        Status = "ready";
        CreatedAt = DateTime.UtcNow;
    }
}