namespace VoiceCode.OrchestratorService.Configuration;

public class OrchestrationOptions
{
    public bool EnableVoiceSummaries { get; set; } = true;
    public bool EnableTaskPlanning { get; set; } = false; // Phase 4
    public bool EnableMultiAgent { get; set; } = false; // Phase 3
    public int MaxSummaryLength { get; set; } = 200; // words
    public int MaxKeyActions { get; set; } = 5;
    public ResponseDetailLevel DefaultDetailLevel { get; set; } = ResponseDetailLevel.Summary;
    public Dictionary<string, AgentConfig> Agents { get; set; } = new();
    public List<string> WorkerEndpoints { get; set; } = new();
    public int TaskTimeoutMinutes { get; set; } = 30;
    public bool EnableWorkerPool { get; set; } = false;
    public int MaxConcurrentWorkers { get; set; } = 5;
}

public enum ResponseDetailLevel
{
    Minimal,    // Just confirmation
    Summary,    // Key actions and outcomes
    Detailed,   // Include technical details
    Full        // Everything (for debugging)
}

public class AgentConfig
{
    public string Name { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 30;
}

public class ServiceEndpoints
{
    public string ClaudeService { get; set; } = "http://localhost:5005";
    public string TTSService { get; set; } = "http://localhost:5003";
    public string RouterService { get; set; } = "http://localhost:5002";
}