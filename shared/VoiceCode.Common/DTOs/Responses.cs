using VoiceCode.Common.Enums;
using VoiceCode.Common.Models;

namespace VoiceCode.Common.DTOs;

public class ProcessingResponse
{
    public string RequestId { get; set; } = string.Empty;
    public ProcessingStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? EstimatedCompletionTime { get; set; }
}

public class CodeResultResponse
{
    public string Id { get; set; } = string.Empty;
    public List<CodeBlockDto> CodeBlocks { get; set; } = new();
    public string Explanation { get; set; } = string.Empty;
    public List<string> SuggestedFiles { get; set; } = new();
    public List<string> Dependencies { get; set; } = new();
    public string? AudioUrl { get; set; }
}

public class CodeBlockDto
{
    public string Language { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? FileName { get; set; }
}

public class StatusResponse
{
    public string RequestId { get; set; } = string.Empty;
    public ProcessingStatus Status { get; set; }
    public int Progress { get; set; }
    public object? Result { get; set; }
    public string? Error { get; set; }
}

public class AudioResult
{
    public bool Success { get; set; }
    public string AudioUrl { get; set; } = string.Empty;
    public byte[]? AudioData { get; set; }
    public double DurationMs { get; set; }
    public bool FromCache { get; set; }
}

public class RoutingResult
{
    public Route Route { get; set; } = new();
    public Intent Intent { get; set; } = new();
    public string? MessageId { get; set; }
    public string? EnhancedPrompt { get; set; }
    public MessagePriority Priority { get; set; } = MessagePriority.Normal;
    public string? Error { get; set; }
}

public class Route
{
    public string QueueName { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
}

public class DispatchResult
{
    public bool Success { get; set; }
    public List<DispatchChannel> Channels { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

public class DispatchChannel
{
    public string Name { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? MessageId { get; set; }
    public string? NotificationId { get; set; }
}