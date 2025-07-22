namespace VoiceCode.DispatcherService.Configuration;

public class ServiceEndpoints
{
    public string STTService { get; set; } = string.Empty;
    public string ClaudeService { get; set; } = string.Empty;
    public string RouterService { get; set; } = string.Empty;
    public string GeneratorService { get; set; } = string.Empty;
    public string TTSService { get; set; } = string.Empty;
}

public class DispatcherOptions
{
    public int MaxConcurrentSessions { get; set; } = 1000;
    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan SessionCleanupInterval { get; set; } = TimeSpan.FromMinutes(5);
    public int MaxMessagesPerSession { get; set; } = 1000;
    public int MaxMessageSize { get; set; } = 1024 * 1024; // 1MB
    public bool EnableMetrics { get; set; } = true;
    public bool EnableMessageLogging { get; set; } = false;
}