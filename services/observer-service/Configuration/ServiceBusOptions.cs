namespace VoiceCode.ObserverService.Configuration;

public class ServiceBusOptions
{
    public const string SectionName = "ServiceBus";
    
    public string ConnectionString { get; set; } = string.Empty;
    public string StreamQueueName { get; set; } = "sdk-stream-events";
    public string NarrationQueueName { get; set; } = "tts-requests";
    public int MaxConcurrentMessages { get; set; } = 5;
    public int PrefetchCount { get; set; } = 10;
}