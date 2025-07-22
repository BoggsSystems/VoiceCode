namespace VoiceCode.STTService.Configuration;

public class AzureSpeechOptions
{
    public string Key { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public bool UseCustomEndpoint { get; set; }
    public int MaxConcurrentRecognitions { get; set; } = 100;
    public TimeSpan RecognitionTimeout { get; set; } = TimeSpan.FromSeconds(30);
}