using System.Text;
using Microsoft.Extensions.Options;
using VoiceCode.ObserverService.Configuration;
using VoiceCode.ObserverService.Models;

namespace VoiceCode.ObserverService.Services;

public class NarrationGenerator : INarrationGenerator
{
    private readonly ILogger<NarrationGenerator> _logger;
    private readonly IOpenAIService _openAiService;
    private readonly ObserverOptions _options;

    private const string SystemPrompt = @"You are an AI assistant observing a code generation system in action. 
Your job is to provide brief, natural, and helpful narrations of what's happening.
Keep responses under 20 words when possible. Be conversational and friendly.
Focus on the outcome and purpose, not technical details.
Examples:
- Instead of 'Executing file write operation on index.tsx', say 'Creating your React component now.'
- Instead of 'Analyzing repository structure', say 'Let me look at your project setup.'
- Instead of 'Error in parsing syntax', say 'I found an issue. Let me fix that.'";

    public NarrationGenerator(
        ILogger<NarrationGenerator> logger,
        IOpenAIService openAiService,
        IOptions<ObserverOptions> options)
    {
        _logger = logger;
        _openAiService = openAiService;
        _options = options.Value;
    }

    public bool ShouldNarrate(SdkStreamEvent streamEvent)
    {
        // Check if this operation type is ignored
        if (_options.IgnoredOperations.Contains(streamEvent.EventType))
        {
            return false;
        }

        // Always narrate errors and important events
        if (streamEvent.EventType is SdkEventTypes.Error or SdkEventTypes.FileCreate or SdkEventTypes.CodeGeneration)
        {
            return true;
        }

        // Skip very frequent events
        if (streamEvent.EventType == SdkEventTypes.Progress && streamEvent.Metadata.ContainsKey("percentage"))
        {
            if (streamEvent.Metadata["percentage"] is double percentage)
            {
                // Only narrate at key milestones
                return percentage is 0 or 25 or 50 or 75 or 100;
            }
        }

        return true;
    }

    public async Task<string?> GenerateNarrationAsync(SdkStreamEvent streamEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            var prompt = BuildPrompt(streamEvent);
            
            if (_options.EnableDebugLogging)
            {
                _logger.LogDebug("Generating narration for event {EventType} with prompt: {Prompt}", 
                    streamEvent.EventType, prompt);
            }

            var narration = await _openAiService.GenerateCompletionAsync(
                $"{SystemPrompt}\n\nEvent to narrate: {prompt}", 
                cancellationToken);

            return narration;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating narration for event {EventType}", streamEvent.EventType);
            return GetFallbackNarration(streamEvent);
        }
    }

    private string BuildPrompt(SdkStreamEvent streamEvent)
    {
        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine($"Event Type: {streamEvent.EventType}");
        promptBuilder.AppendLine($"Operation: {streamEvent.Operation}");
        
        if (!string.IsNullOrWhiteSpace(streamEvent.Details))
        {
            promptBuilder.AppendLine($"Details: {streamEvent.Details}");
        }

        // Add relevant metadata
        if (streamEvent.Metadata.Count > 0)
        {
            promptBuilder.AppendLine("Context:");
            foreach (var (key, value) in streamEvent.Metadata)
            {
                if (key is "fileName" or "componentName" or "errorMessage" or "percentage")
                {
                    promptBuilder.AppendLine($"- {key}: {value}");
                }
            }
        }

        return promptBuilder.ToString();
    }

    private string? GetFallbackNarration(SdkStreamEvent streamEvent)
    {
        return streamEvent.EventType switch
        {
            SdkEventTypes.FileRead => "Looking at your code...",
            SdkEventTypes.FileWrite => "Writing the changes...",
            SdkEventTypes.FileCreate => "Creating a new file...",
            SdkEventTypes.FileDelete => "Removing that file...",
            SdkEventTypes.DirectoryCreate => "Setting up the folder structure...",
            SdkEventTypes.CodeAnalysis => "Analyzing your code...",
            SdkEventTypes.CodeGeneration => "Generating the code now...",
            SdkEventTypes.CommandExecution => "Running the command...",
            SdkEventTypes.Error => "I encountered an issue. Let me handle that...",
            SdkEventTypes.Progress => "Still working on it...",
            SdkEventTypes.Thinking => "Let me think about this...",
            _ => null
        };
    }
}