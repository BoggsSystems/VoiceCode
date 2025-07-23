namespace VoiceCode.Common.DTOs;

public class ClaudeRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Type { get; set; } = "generate"; // generate, explain, fix, refactor, review, test
    public string Prompt { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public int MaxTokens { get; set; } = 4096;
    public double Temperature { get; set; } = 0.7;
    public string Model { get; set; } = "claude-3-opus-20240229";
    public string? SessionId { get; set; }
    public List<ClaudeMessage> Messages { get; set; } = new();
    public Dictionary<string, object> Context { get; set; } = new();
}

public class ClaudeMessage
{
    public string Role { get; set; } = string.Empty; // "user", "assistant", "system"
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public class ClaudeResponse
{
    public string Id { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int TokensUsed { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public DateTime ProcessedAt { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public bool Success { get; set; } = true;
    public string? Error { get; set; }
    public VoiceResponseData? VoiceResponse { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public List<ClaudeCodeBlock> CodeBlocks { get; set; } = new();
}

public class ClaudeCodeBlock
{
    public string Language { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
}

public class VoiceResponseData
{
    public string Text { get; set; } = string.Empty;
    public string? Emotion { get; set; }
    public bool IncludeCodeSummary { get; set; } = true;
}