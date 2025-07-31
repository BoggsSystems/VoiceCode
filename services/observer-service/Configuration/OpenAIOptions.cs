namespace VoiceCode.ObserverService.Configuration;

public class OpenAIOptions
{
    public const string SectionName = "OpenAI";
    
    public string ApiKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string DeploymentName { get; set; } = "gpt-4";
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 150;
}