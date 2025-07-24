namespace VoiceCode.OrchestratorService.Models;

public class OrchestrationRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Transcript { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public Dictionary<string, object> Context { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public ResponseDetailLevel? RequestedDetailLevel { get; set; }
}

public class OrchestrationResponse
{
    public string SessionId { get; set; } = string.Empty;
    public VoiceSummary VoiceSummary { get; set; } = new();
    public string FullResponse { get; set; } = string.Empty;
    public List<CodeChange> CodeChanges { get; set; } = new();
    public OrchestrationMetadata Metadata { get; set; } = new();
}

public class VoiceSummary
{
    public string BriefDescription { get; set; } = string.Empty;
    public List<string> KeyActions { get; set; } = new();
    public string? NextSteps { get; set; }
    public bool RequiresConfirmation { get; set; }
    public ConfirmationPrompt? Confirmation { get; set; }
    public Dictionary<string, object> AdditionalContext { get; set; } = new();
}

public class ConfirmationPrompt
{
    public string Question { get; set; } = string.Empty;
    public List<string> Options { get; set; } = new();
    public string DefaultOption { get; set; } = string.Empty;
    public string WarningMessage { get; set; } = string.Empty;
}

public class CodeChange
{
    public string FileName { get; set; } = string.Empty;
    public string ChangeType { get; set; } = string.Empty; // Created, Modified, Deleted
    public string Summary { get; set; } = string.Empty;
    public int LinesAdded { get; set; }
    public int LinesRemoved { get; set; }
    public List<string> KeyElements { get; set; } = new(); // Classes, functions, etc.
}

public class OrchestrationMetadata
{
    public string AgentUsed { get; set; } = "Claude"; // For now
    public int ProcessingTimeMs { get; set; }
    public int TokensUsed { get; set; }
    public double ConfidenceScore { get; set; }
    public List<string> WarningsOrErrors { get; set; } = new();
}