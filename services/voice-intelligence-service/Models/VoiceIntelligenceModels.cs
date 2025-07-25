namespace VoiceCode.VoiceIntelligenceService.Models;

public class WorkerResult
{
    public string TaskId { get; set; } = string.Empty;
    public string WorkerId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public Dictionary<string, object> Metadata { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class VoiceResponse
{
    public string TaskId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string SpokenResponse { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> WorkersInvolved { get; set; } = new();
    public Dictionary<string, object> Context { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class ConversationContext
{
    public string SessionId { get; set; } = string.Empty;
    public string OriginalCommand { get; set; } = string.Empty;
    public List<WorkerResult> WorkerResults { get; set; } = new();
    public List<string> PreviousResponses { get; set; } = new();
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
}

public class SynthesisRequest
{
    public string TaskId { get; set; } = string.Empty;
    public string OriginalVoiceCommand { get; set; } = string.Empty;
    public List<WorkerResult> WorkerResults { get; set; } = new();
    public ConversationContext? Context { get; set; }
}