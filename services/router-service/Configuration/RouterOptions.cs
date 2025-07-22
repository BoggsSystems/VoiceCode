namespace VoiceCode.RouterService.Configuration;

public class RouterOptions
{
    public Dictionary<string, RouteConfiguration> Routes { get; set; } = new();
    public int MaxContextHistoryItems { get; set; } = 10;
    public TimeSpan ContextTimeout { get; set; } = TimeSpan.FromMinutes(30);
    public bool EnableSmartRouting { get; set; } = true;
}

public class RouteConfiguration
{
    public string QueueName { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public int Priority { get; set; } = 0;
    public TimeSpan TimeToLive { get; set; } = TimeSpan.FromMinutes(5);
}

public class IntentClassificationOptions
{
    public string TextAnalyticsEndpoint { get; set; } = string.Empty;
    public string TextAnalyticsKey { get; set; } = string.Empty;
    public double ConfidenceThreshold { get; set; } = 0.7;
    public bool UseMLModel { get; set; } = true;
    public string ModelPath { get; set; } = "Models/intent-classifier.zip";
    public bool EnableFallbackClassification { get; set; } = true;
}