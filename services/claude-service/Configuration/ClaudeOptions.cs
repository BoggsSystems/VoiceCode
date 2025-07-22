using VoiceCode.Common.DTOs;

namespace VoiceCode.ClaudeService.Configuration;

public class ClaudeOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.anthropic.com";
    public string Model { get; set; } = "claude-3-opus-20240229";
    public int MaxTokens { get; set; } = 4000;
    public double Temperature { get; set; } = 0.7;
    public int MaxRetries { get; set; } = 3;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);
}

public class ResponseInterpreterOptions
{
    public string Model { get; set; } = "gpt-3.5-turbo";
    public string ApiKey { get; set; } = string.Empty;
    public PersonalityProfile DefaultPersonality { get; set; } = PersonalityProfile.FriendlyAssistant;
    public int MaxSummaryLength { get; set; } = 50; // words
}

