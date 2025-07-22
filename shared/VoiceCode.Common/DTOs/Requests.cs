using System.ComponentModel.DataAnnotations;

namespace VoiceCode.Common.DTOs;

public class ProcessVoiceCommandRequest
{
    [Required]
    public byte[] AudioData { get; set; } = Array.Empty<byte>();
    
    [Required]
    public string UserId { get; set; } = string.Empty;
    
    [Required]
    public string SessionId { get; set; } = string.Empty;
    
    public string Language { get; set; } = "en-US";
    
    public Dictionary<string, object> Context { get; set; } = new();
}

public class GenerateCodeRequest
{
    [Required]
    [MinLength(3)]
    public string Instruction { get; set; } = string.Empty;
    
    [Required]
    public string Language { get; set; } = string.Empty;
    
    public string? Context { get; set; }
    
    public string? Style { get; set; }
    
    [Range(100, 8000)]
    public int MaxTokens { get; set; } = 4000;
}

public class UpdatePreferencesRequest
{
    public string? PreferredLanguage { get; set; }
    public string? CodingStyle { get; set; }
    public string? PreferredIDE { get; set; }
    public VoiceSettings? VoiceSettings { get; set; }
    public NotificationSettings? NotificationSettings { get; set; }
}

public class RoutingRequest
{
    [Required]
    public string Transcript { get; set; } = string.Empty;
    
    [Required]
    public string UserId { get; set; } = string.Empty;
    
    [Required]
    public string SessionId { get; set; } = string.Empty;
}

public class TTSRequest
{
    [Required]
    public string Text { get; set; } = string.Empty;
    
    public string Voice { get; set; } = "en-US-JennyNeural";
    
    public string SessionId { get; set; } = string.Empty;
    
    public SpeechStyle? SpeechStyle { get; set; }
}

public class SpeechStyle
{
    public string Rate { get; set; } = "1.0";
    public string Pitch { get; set; } = "+0%";
    public string Emphasis { get; set; } = "moderate";
}