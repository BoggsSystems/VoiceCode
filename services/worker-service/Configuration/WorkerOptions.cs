namespace VoiceCode.WorkerService.Configuration;

public class WorkerOptions
{
    public const string SectionName = "Worker";
    
    public string ClaudeApiKey { get; set; } = string.Empty;
    public string ClaudeModel { get; set; } = "claude-3-opus-20240229";
    public string WorkspaceBasePath { get; set; } = "/workspaces";
    public int MaxConcurrentWorkers { get; set; } = 5;
    public int WorkerTimeoutMinutes { get; set; } = 30;
    public bool EnableMcpServer { get; set; } = true;
    public string McpTransport { get; set; } = "stdio"; // stdio or http
    public int McpServerPort { get; set; } = 3000;
    public string StreamQueueName { get; set; } = "sdk-stream-events";
    public bool EnableDebugLogging { get; set; } = false;
    public string SidecarUrl { get; set; } = "http://localhost:3000";
    public bool UseSidecar { get; set; } = true;
}