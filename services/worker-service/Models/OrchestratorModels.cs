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