using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.RegularExpressions;
using VoiceCode.ClaudeService.Configuration;
using VoiceCode.Common.DTOs;

namespace VoiceCode.ClaudeService.Services;

public interface IResponseInterpreterService
{
    Task<VoiceResponse> InterpretForVoiceAsync(string claudeResponse, string context, PersonalityProfile? personality = null);
    Task<string> GetProgressUpdateAsync(string claudeResponse);
    Task<string> GetErrorSummaryAsync(string error, string context);
}

public class ResponseInterpreterService : IResponseInterpreterService
{
    private readonly OpenAIClient _openAIClient;
    private readonly ResponseInterpreterOptions _options;
    private readonly ILogger<ResponseInterpreterService> _logger;
    private readonly Dictionary<PersonalityProfile, PersonalityTraits> _personalities;

    public ResponseInterpreterService(
        IOptions<ResponseInterpreterOptions> options,
        ILogger<ResponseInterpreterService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _openAIClient = new OpenAIClient(_options.ApiKey);
        
        _personalities = InitializePersonalities();
    }

    public async Task<VoiceResponse> InterpretForVoiceAsync(
        string claudeResponse, 
        string context, 
        PersonalityProfile? personality = null)
    {
        try
        {
            var activePersonality = personality ?? _options.DefaultPersonality;
            var traits = _personalities[activePersonality];

            // Analyze the Claude response
            var analysis = AnalyzeResponse(claudeResponse);
            
            var systemPrompt = $@"You are a voice assistant with this personality: {traits.Description}
Your speech style: {traits.SpeechStyle}
Maximum response length: {_options.MaxSummaryLength} words

Convert the following technical response into a brief, natural voice response.
Include: {string.Join(", ", traits.FocusPoints)}
Context: {context}";

            var chatCompletionsOptions = new ChatCompletionsOptions
            {
                DeploymentName = _options.Model,
                Messages =
                {
                    new ChatRequestSystemMessage(systemPrompt),
                    new ChatRequestUserMessage($"Technical response: {claudeResponse}")
                },
                Temperature = 0.7f,
                MaxTokens = 100
            };

            var response = await _openAIClient.GetChatCompletionsAsync(chatCompletionsOptions);
            var voiceText = response.Value.Choices[0].Message.Content;

            // Add personality-specific variations
            voiceText = ApplyPersonalityVariations(voiceText, traits, analysis);

            return new VoiceResponse
            {
                Text = voiceText,
                Personality = activePersonality,
                Metadata = new VoiceMetadata
                {
                    FilesCreated = analysis.FilesCreated,
                    FilesModified = analysis.FilesModified,
                    ErrorsFound = analysis.ErrorsFound,
                    IsComplete = analysis.IsComplete,
                    ProgressPercent = analysis.ProgressPercent
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to interpret response for voice");
            return new VoiceResponse
            {
                Text = "I've completed the task. Check the screen for details.",
                Personality = personality ?? _options.DefaultPersonality
            };
        }
    }

    public async Task<string> GetProgressUpdateAsync(string claudeResponse)
    {
        var analysis = AnalyzeResponse(claudeResponse);
        
        if (analysis.IsComplete)
        {
            return analysis.ErrorsFound > 0 
                ? $"Finished with {analysis.ErrorsFound} issues to address"
                : "All done!";
        }

        if (analysis.FilesCreated > 0 || analysis.FilesModified > 0)
        {
            var action = analysis.FilesCreated > 0 ? "Created" : "Updated";
            var count = Math.Max(analysis.FilesCreated, analysis.FilesModified);
            return $"{action} {count} file{(count > 1 ? "s" : "")}";
        }

        if (analysis.ProgressPercent > 0)
        {
            return $"About {analysis.ProgressPercent}% complete";
        }

        return "Working on it...";
    }

    public async Task<string> GetErrorSummaryAsync(string error, string context)
    {
        var errorType = IdentifyErrorType(error);
        
        return errorType switch
        {
            "syntax" => "Syntax error in the code",
            "build" => "Build failed",
            "test" => "Tests are failing",
            "dependency" => "Missing dependency",
            "permission" => "Permission issue",
            _ => "Hit an error"
        };
    }

    private ResponseAnalysis AnalyzeResponse(string response)
    {
        var analysis = new ResponseAnalysis();

        // Count file operations
        analysis.FilesCreated = Regex.Matches(response, @"(created?|new file|wrote)", RegexOptions.IgnoreCase).Count;
        analysis.FilesModified = Regex.Matches(response, @"(modified|updated|changed)", RegexOptions.IgnoreCase).Count;
        
        // Check for errors
        analysis.ErrorsFound = Regex.Matches(response, @"(error|exception|failed|issue)", RegexOptions.IgnoreCase).Count;
        
        // Check completion status
        analysis.IsComplete = Regex.IsMatch(response, @"(complete|finished|done|ready)", RegexOptions.IgnoreCase);
        
        // Estimate progress
        if (response.Contains("%"))
        {
            var percentMatch = Regex.Match(response, @"(\d+)%");
            if (percentMatch.Success)
            {
                analysis.ProgressPercent = int.Parse(percentMatch.Groups[1].Value);
            }
        }

        return analysis;
    }

    private string ApplyPersonalityVariations(string text, PersonalityTraits traits, ResponseAnalysis analysis)
    {
        // Add personality-specific prefixes or suffixes
        if (analysis.IsComplete && traits.CompletionPhrases.Any())
        {
            var phrase = traits.CompletionPhrases[Random.Shared.Next(traits.CompletionPhrases.Count)];
            text = $"{phrase} {text}";
        }

        if (analysis.ErrorsFound > 0 && traits.ErrorPhrases.Any())
        {
            var phrase = traits.ErrorPhrases[Random.Shared.Next(traits.ErrorPhrases.Count)];
            text = $"{phrase} {text}";
        }

        return text;
    }

    private string IdentifyErrorType(string error)
    {
        if (Regex.IsMatch(error, @"syntax|parse|unexpected", RegexOptions.IgnoreCase))
            return "syntax";
        if (Regex.IsMatch(error, @"build|compile|MSBuild", RegexOptions.IgnoreCase))
            return "build";
        if (Regex.IsMatch(error, @"test|assert|fail", RegexOptions.IgnoreCase))
            return "test";
        if (Regex.IsMatch(error, @"package|dependency|reference", RegexOptions.IgnoreCase))
            return "dependency";
        if (Regex.IsMatch(error, @"permission|access|denied", RegexOptions.IgnoreCase))
            return "permission";
        return "unknown";
    }

    private Dictionary<PersonalityProfile, PersonalityTraits> InitializePersonalities()
    {
        return new Dictionary<PersonalityProfile, PersonalityTraits>
        {
            [PersonalityProfile.FriendlyAssistant] = new PersonalityTraits
            {
                Description = "Helpful and encouraging coding assistant",
                SpeechStyle = "Warm, supportive, uses 'we' language",
                FocusPoints = new[] { "progress made", "next steps", "encouragement" },
                CompletionPhrases = new List<string> { "Great!", "Nice work!", "Perfect!" },
                ErrorPhrases = new List<string> { "No worries,", "Let me fix that," }
            },
            [PersonalityProfile.ProfessionalCoPilot] = new PersonalityTraits
            {
                Description = "Efficient professional developer",
                SpeechStyle = "Clear, technical, direct",
                FocusPoints = new[] { "status", "technical details", "efficiency" },
                CompletionPhrases = new List<string> { "Complete.", "Task finished.", "Done." },
                ErrorPhrases = new List<string> { "Error detected:", "Issue found:" }
            },
            [PersonalityProfile.CasualBuddy] = new PersonalityTraits
            {
                Description = "Laid-back coding buddy",
                SpeechStyle = "Casual, friendly, uses contractions",
                FocusPoints = new[] { "what's happening", "keeping it simple" },
                CompletionPhrases = new List<string> { "All set!", "Good to go!", "That's done!" },
                ErrorPhrases = new List<string> { "Oops,", "Uh oh," }
            },
            [PersonalityProfile.Minimalist] = new PersonalityTraits
            {
                Description = "Ultra-concise responses",
                SpeechStyle = "Minimal words, essential info only",
                FocusPoints = new[] { "action taken", "result" },
                CompletionPhrases = new List<string> { "", "", "" },
                ErrorPhrases = new List<string> { "Error:", "Failed:" }
            }
        };
    }
}

public class PersonalityTraits
{
    public string Description { get; set; } = string.Empty;
    public string SpeechStyle { get; set; } = string.Empty;
    public string[] FocusPoints { get; set; } = Array.Empty<string>();
    public List<string> CompletionPhrases { get; set; } = new();
    public List<string> ErrorPhrases { get; set; } = new();
}

public class ResponseAnalysis
{
    public int FilesCreated { get; set; }
    public int FilesModified { get; set; }
    public int ErrorsFound { get; set; }
    public bool IsComplete { get; set; }
    public int ProgressPercent { get; set; }
}

