using System.Text;
using VoiceCode.Common.Models;
using VoiceCode.Common.Enums;

namespace VoiceCode.RouterService.Services;

public interface IPromptEnhancer
{
    Task<string> EnhancePromptAsync(string transcript, Intent intent, UserContext context);
}

public class PromptEnhancerService : IPromptEnhancer
{
    private readonly ILogger<PromptEnhancerService> _logger;

    public PromptEnhancerService(ILogger<PromptEnhancerService> logger)
    {
        _logger = logger;
    }

    public Task<string> EnhancePromptAsync(string transcript, Intent intent, UserContext context)
    {
        try
        {
            var enhancedPrompt = new StringBuilder();

            // Add context from recent interactions
            AddRecentContext(enhancedPrompt, context, intent);

            // Add user preferences
            AddUserPreferences(enhancedPrompt, context.Preferences, intent);

            // Add session context
            AddSessionContext(enhancedPrompt, context.SessionData, intent);

            // Add the original request
            enhancedPrompt.AppendLine($"Current request: {transcript}");

            // Add intent-specific enhancements
            AddIntentSpecificEnhancements(enhancedPrompt, intent, context);

            var result = enhancedPrompt.ToString();
            _logger.LogDebug("Enhanced prompt from {OriginalLength} to {EnhancedLength} characters",
                transcript.Length, result.Length);

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enhance prompt");
            return Task.FromResult(transcript);
        }
    }

    private void AddRecentContext(StringBuilder prompt, UserContext context, Intent currentIntent)
    {
        if (!context.RecentInteractions.Any())
            return;

        // Find relevant recent interactions
        var relevantInteractions = context.RecentInteractions
            .Where(i => IsRelevantInteraction(i, currentIntent))
            .Take(3)
            .ToList();

        if (relevantInteractions.Any())
        {
            prompt.AppendLine("Recent context:");
            foreach (var interaction in relevantInteractions)
            {
                prompt.AppendLine($"- Previously: {interaction.Transcript}");
                if (!string.IsNullOrEmpty(interaction.Result))
                {
                    prompt.AppendLine($"  Result: {TruncateResult(interaction.Result)}");
                }
            }
            prompt.AppendLine();
        }
    }

    private void AddUserPreferences(StringBuilder prompt, UserPreferences preferences, Intent intent)
    {
        if (intent.Category is IntentCategory.CodeGeneration or IntentCategory.CodeRefactoring)
        {
            prompt.AppendLine("User preferences:");
            prompt.AppendLine($"- Preferred language: {preferences.PreferredLanguage}");
            prompt.AppendLine($"- Coding style: {preferences.CodingStyle}");
            prompt.AppendLine($"- Indentation: {preferences.IndentationSize} {preferences.IndentationStyle}");
            
            if (preferences.PreferExplicitTypes)
                prompt.AppendLine("- Use explicit types rather than var/auto");
            
            if (preferences.PreferAsyncMethods)
                prompt.AppendLine("- Prefer async/await patterns");
            
            prompt.AppendLine();
        }
    }

    private void AddSessionContext(StringBuilder prompt, Dictionary<string, object> sessionData, Intent intent)
    {
        if (!sessionData.Any())
            return;

        var relevantData = new List<string>();

        // Add current component context
        if (sessionData.TryGetValue("current_component", out var component))
        {
            relevantData.Add($"Working on: {component}");
        }

        // Add current language context
        if (sessionData.TryGetValue("current_language", out var language))
        {
            relevantData.Add($"Language: {language}");
        }

        // Add error context if relevant
        if (intent.Category == IntentCategory.ErrorFixing && 
            sessionData.TryGetValue("has_active_error", out var hasError) && 
            (bool)hasError)
        {
            if (sessionData.TryGetValue("error_type", out var errorType))
            {
                relevantData.Add($"Previous error type: {errorType}");
            }
        }

        // Add project context
        if (sessionData.TryGetValue("current_project", out var project))
        {
            relevantData.Add($"Project: {project}");
        }

        if (relevantData.Any())
        {
            prompt.AppendLine("Session context:");
            foreach (var data in relevantData)
            {
                prompt.AppendLine($"- {data}");
            }
            prompt.AppendLine();
        }
    }

    private void AddIntentSpecificEnhancements(StringBuilder prompt, Intent intent, UserContext context)
    {
        switch (intent.Category)
        {
            case IntentCategory.CodeGeneration:
                prompt.AppendLine("Please generate production-ready code with:");
                prompt.AppendLine("- Proper error handling");
                prompt.AppendLine("- Clear variable and function names");
                prompt.AppendLine("- Appropriate comments for complex logic");
                break;

            case IntentCategory.ErrorFixing:
                prompt.AppendLine("When fixing the error:");
                prompt.AppendLine("- Identify the root cause");
                prompt.AppendLine("- Provide the corrected code");
                prompt.AppendLine("- Explain what was wrong");
                prompt.AppendLine("- Suggest how to prevent similar issues");
                break;

            case IntentCategory.CodeRefactoring:
                prompt.AppendLine("When refactoring:");
                prompt.AppendLine("- Maintain existing functionality");
                prompt.AppendLine("- Improve code readability");
                prompt.AppendLine("- Apply SOLID principles where appropriate");
                prompt.AppendLine("- Document significant changes");
                break;

            case IntentCategory.Testing:
                prompt.AppendLine("For test generation:");
                prompt.AppendLine("- Include unit tests for core functionality");
                prompt.AppendLine("- Test edge cases and error conditions");
                prompt.AppendLine("- Use appropriate testing framework");
                prompt.AppendLine("- Include clear test descriptions");
                break;

            case IntentCategory.Documentation:
                prompt.AppendLine("For documentation:");
                prompt.AppendLine("- Include clear descriptions");
                prompt.AppendLine("- Add usage examples");
                prompt.AppendLine("- Document parameters and return values");
                prompt.AppendLine("- Note any prerequisites or dependencies");
                break;
        }
    }

    private bool IsRelevantInteraction(ContextEntry interaction, Intent currentIntent)
    {
        // Same category is always relevant
        if (interaction.Intent.Category == currentIntent.Category)
            return true;

        // Error fixing is relevant to the code that caused the error
        if (currentIntent.Category == IntentCategory.ErrorFixing && 
            interaction.Intent.Category == IntentCategory.CodeGeneration)
            return true;

        // Testing is relevant to recently generated code
        if (currentIntent.Category == IntentCategory.Testing && 
            interaction.Intent.Category == IntentCategory.CodeGeneration)
            return true;

        // Documentation is relevant to recently generated or refactored code
        if (currentIntent.Category == IntentCategory.Documentation && 
            (interaction.Intent.Category == IntentCategory.CodeGeneration || 
             interaction.Intent.Category == IntentCategory.CodeRefactoring))
            return true;

        return false;
    }

    private string TruncateResult(string result)
    {
        const int maxLength = 100;
        if (result.Length <= maxLength)
            return result;

        return result.Substring(0, maxLength) + "...";
    }
}