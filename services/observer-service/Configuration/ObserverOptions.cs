namespace VoiceCode.ObserverService.Configuration;

public class ObserverOptions
{
    public const string SectionName = "Observer";
    
    public string ServiceName { get; set; } = "ObserverService";
    public int MaxConcurrentStreams { get; set; } = 10;
    public int NarrationThrottleMs { get; set; } = 2000;
    public bool EnableDebugLogging { get; set; } = false;
    public string[] IgnoredOperations { get; set; } = Array.Empty<string>();
}