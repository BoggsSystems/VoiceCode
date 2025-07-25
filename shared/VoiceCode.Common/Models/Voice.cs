namespace VoiceCode.Common.Models;

public class VoiceCommand
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public byte[] AudioData { get; set; } = Array.Empty<byte>();
    public string AudioFormat { get; set; } = "wav";
    public int SampleRate { get; set; } = 16000;
    public string Language { get; set; } = "en-US";
    public Dictionary<string, object> Context { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class TranscriptionResult
{
    public string Id { get; set; } = string.Empty;
    public string Transcript { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string Language { get; set; } = string.Empty;
    public double DurationMs { get; set; }
    public bool Success { get; set; } = true;
    public string? Error { get; set; }
    public List<TranscriptionAlternative> Alternatives { get; set; } = new();
    public List<TranscriptionWord> Words { get; set; } = new();
    public string? AudioUrl { get; set; }
    public DateTime? RequestedAt { get; set; }
    public string? UserId { get; set; }
    
    // Streaming-specific properties
    public string? SessionId { get; set; }
    public bool IsFinal { get; set; } = true;
    public long? AudioSizeBytes { get; set; }
    public double? Duration { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    
    // Backward compatibility property
    public string Text 
    { 
        get => Transcript; 
        set => Transcript = value; 
    }
}

public class TranscriptionAlternative
{
    public string Transcript { get; set; } = string.Empty;
    public double Confidence { get; set; }
    
    // Backward compatibility property
    public string Text 
    { 
        get => Transcript; 
        set => Transcript = value; 
    }
}

public class TranscriptionWord
{
    public string Word { get; set; } = string.Empty;
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public double Confidence { get; set; }
}

public class TranscriptionSegment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Text { get; set; } = string.Empty;
    public bool IsFinal { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public double Confidence { get; set; }
    public List<TranscriptionWord> Words { get; set; } = new();
    public string? Language { get; set; }
    public TimeSpan? Duration { get; set; }
}

public class PartialTranscriptionResult
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Text { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? SessionId { get; set; }
    public double? StabilityScore { get; set; }
}