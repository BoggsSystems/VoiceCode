namespace VoiceCode.WorkerService.Models;

// Model that matches what the orchestrator sends
public class OrchestratorTask
{
    public string TaskId { get; set; } = "";
    public string WorkerId { get; set; } = "";
    public string VoiceCommand { get; set; } = "";
    public string SessionId { get; set; } = "";
    public DateTime Timestamp { get; set; }
}

// Model that matches what the Dispatcher actually sends
public class WorkerTaskPayload
{
    public string TaskId { get; set; } = "";
    public string Command { get; set; } = "";
    public WorkerContext Context { get; set; } = new();
    public string ResponseChannel { get; set; } = "";
}

public class WorkerContext
{
    public int WorkerNumber { get; set; }
    public string? UserId { get; set; }
    public string? SessionId { get; set; }
    public string? OriginalTranscription { get; set; }
}