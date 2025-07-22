namespace VoiceCode.Common.DTOs;

public class CodeGenerationRequest
{
    public string Type { get; set; } = "generate"; // generate, explain, fix, refactor, review, test
    public string Code { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public List<string>? Examples { get; set; }
    public Dictionary<string, object>? Context { get; set; }
}

public class CodeGenerationResponse
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public int Tokens { get; set; }
    public string Model { get; set; } = string.Empty;
    public DateTime ProcessingTime { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public string? RequestId { get; set; }
    public string? RawResponse { get; set; }
    public VoiceResponse? VoiceResponse { get; set; }
}

public class VoiceResponse
{
    public string Text { get; set; } = string.Empty;
    public PersonalityProfile Personality { get; set; }
    public VoiceMetadata? Metadata { get; set; }
}

public class VoiceMetadata
{
    public int FilesCreated { get; set; }
    public int FilesModified { get; set; }
    public int ErrorsFound { get; set; }
    public bool IsComplete { get; set; }
    public int ProgressPercent { get; set; }
}

public enum PersonalityProfile
{
    FriendlyAssistant,
    ProfessionalCoPilot,
    CasualBuddy,
    Minimalist
}