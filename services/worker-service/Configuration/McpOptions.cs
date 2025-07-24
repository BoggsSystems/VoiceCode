namespace VoiceCode.WorkerService.Configuration;

public class McpOptions
{
    public const string SectionName = "Mcp";
    
    public string TransportType { get; set; } = "stdio"; // stdio, http
    public string ServerExecutable { get; set; } = "claude-code";
    public string[] ServerArguments { get; set; } = ["api", "--mode", "mcp-server"];
    public int ConnectionTimeoutSeconds { get; set; } = 30;
    public int RequestTimeoutSeconds { get; set; } = 600; // 10 minutes for long operations
    public bool EnableLogging { get; set; } = true;
    public string LogLevel { get; set; } = "Information";
}