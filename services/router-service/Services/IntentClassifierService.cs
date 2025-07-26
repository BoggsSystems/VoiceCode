using System.Text.Json;
using Microsoft.Extensions.Options;
using VoiceCode.Common.Models;
using VoiceCode.RouterService.Configuration;

namespace VoiceCode.RouterService.Services;

public interface IIntentClassifier
{
    Task<IntentClassification> ClassifyAsync(string transcript, Dictionary<string, object>? metadata = null);
    
    // Legacy methods for compatibility
    Task<Intent> ClassifyIntentAsync(string transcript, UserContext context);
    Task<List<IntentAnalysis>> AnalyzeIntentsAsync(string text, string context);
}

public class IntentClassification
{
    public string Intent { get; set; } = string.Empty;
    public string? Product { get; set; }
    public double Confidence { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class IntentClassifierService : IIntentClassifier
{
    private readonly ILogger<IntentClassifierService> _logger;
    private readonly HttpClient _httpClient;
    private readonly IntentClassificationOptions _options;

    public IntentClassifierService(
        ILogger<IntentClassifierService> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<IntentClassificationOptions> options)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
        _options = options.Value;
    }

    public async Task<IntentClassification> ClassifyAsync(string transcript, Dictionary<string, object>? metadata = null)
    {
        try
        {
            _logger.LogInformation("Classifying intent for transcript: {Transcript}", transcript);

            // Prepare OpenAI request
            var request = new
            {
                model = "gpt-4-turbo-preview",
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = BuildSystemPrompt()
                    },
                    new
                    {
                        role = "user",
                        content = BuildUserPrompt(transcript, metadata)
                    }
                },
                temperature = 0.3,
                max_tokens = 200,
                response_format = new { type = "json_object" }
            };

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_options.OpenAIApiKey}");

            var response = await _httpClient.PostAsJsonAsync(
                "https://api.openai.com/v1/chat/completions", 
                request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("OpenAI API error: {StatusCode} - {Error}", response.StatusCode, error);
                return CreateFallbackClassification(transcript);
            }

            var result = await response.Content.ReadFromJsonAsync<OpenAIResponse>();
            var content = result?.Choices?.FirstOrDefault()?.Message?.Content;

            if (string.IsNullOrEmpty(content))
            {
                _logger.LogWarning("Empty response from OpenAI");
                return CreateFallbackClassification(transcript);
            }

            var classification = JsonSerializer.Deserialize<IntentClassification>(content);
            if (classification == null)
            {
                _logger.LogWarning("Failed to deserialize OpenAI response");
                return CreateFallbackClassification(transcript);
            }

            _logger.LogInformation("Intent classified as {Intent} for product {Product} with confidence {Confidence}", 
                classification.Intent, classification.Product, classification.Confidence);

            return classification;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Intent classification failed");
            return CreateFallbackClassification(transcript);
        }
    }

    private string BuildSystemPrompt()
    {
        return @"You are an intent classifier for a voice-controlled development system that manages multiple software products.

Analyze the user's voice command and determine:
1. The intent (what action they want to perform)
2. The product they're referring to (if mentioned)
3. Your confidence level (0.0 to 1.0)
4. Any relevant metadata

Available intents:
- create_feature: Creating new features, screens, components
- modify_feature: Updating existing features
- fix_bug: Fixing errors or issues
- refactor_code: Improving code quality
- add_tests: Creating unit or integration tests
- documentation: Adding or updating documentation
- deploy: Deployment-related tasks
- query_status: Asking about project status
- unclear: When the intent is ambiguous

Common products:
- VoiceCode: The voice coding assistant app
- FinanceTracker: Financial management app
- HealthMonitor: Health tracking app
- EduLearn: Educational platform
- GameHub: Gaming platform

Respond with a JSON object containing:
{
  ""intent"": ""<intent_type>"",
  ""product"": ""<product_name or null>"",
  ""confidence"": <0.0-1.0>,
  ""metadata"": {
    ""feature_type"": ""<if applicable>"",
    ""components"": [""<list of components if mentioned>""],
    ""clarification_needed"": ""<what to ask if unclear>"",
    ""estimated_complexity"": ""<low/medium/high>""
  }
}";
    }

    private string BuildUserPrompt(string transcript, Dictionary<string, object>? metadata)
    {
        var prompt = $"Voice command: \"{transcript}\"";
        
        if (metadata != null && metadata.Any())
        {
            prompt += "\n\nContext:";
            foreach (var item in metadata)
            {
                prompt += $"\n- {item.Key}: {item.Value}";
            }
        }

        return prompt;
    }

    private IntentClassification CreateFallbackClassification(string transcript)
    {
        // Simple fallback logic
        var lowerTranscript = transcript.ToLowerInvariant();
        
        if (lowerTranscript.Contains("create") || lowerTranscript.Contains("add") || lowerTranscript.Contains("new"))
        {
            return new IntentClassification
            {
                Intent = "create_feature",
                Confidence = 0.5,
                Metadata = new Dictionary<string, object>
                {
                    ["fallback"] = true,
                    ["reason"] = "OpenAI unavailable"
                }
            };
        }

        if (lowerTranscript.Contains("fix") || lowerTranscript.Contains("bug") || lowerTranscript.Contains("error"))
        {
            return new IntentClassification
            {
                Intent = "fix_bug",
                Confidence = 0.5,
                Metadata = new Dictionary<string, object>
                {
                    ["fallback"] = true,
                    ["reason"] = "OpenAI unavailable"
                }
            };
        }

        return new IntentClassification
        {
            Intent = "unclear",
            Confidence = 0.3,
            Metadata = new Dictionary<string, object>
            {
                ["fallback"] = true,
                ["reason"] = "OpenAI unavailable",
                ["clarification_needed"] = "Could you please clarify what you'd like me to do?"
            }
        };
    }

    // OpenAI response models
    private class OpenAIResponse
    {
        public List<Choice>? Choices { get; set; }
    }

    private class Choice
    {
        public Message? Message { get; set; }
    }

    private class Message
    {
        public string? Content { get; set; }
    }

    // Legacy method implementations for compatibility
    public async Task<Intent> ClassifyIntentAsync(string transcript, UserContext context)
    {
        var metadata = new Dictionary<string, object>
        {
            ["userId"] = context.UserId ?? "unknown",
            ["sessionId"] = context.SessionId ?? "unknown"
        };

        var classification = await ClassifyAsync(transcript, metadata);

        return new Intent
        {
            Type = classification.Intent,
            Category = MapIntentToCategory(classification.Intent),
            Confidence = classification.Confidence,
            OriginalText = transcript,
            Timestamp = DateTime.UtcNow
        };
    }

    public async Task<List<IntentAnalysis>> AnalyzeIntentsAsync(string text, string context)
    {
        // Simple implementation that analyzes the entire text as one intent
        var classification = await ClassifyAsync(text);
        
        return new List<IntentAnalysis>
        {
            new IntentAnalysis
            {
                Text = text,
                Intent = new Intent
                {
                    Type = classification.Intent,
                    Category = MapIntentToCategory(classification.Intent),
                    Confidence = classification.Confidence,
                    OriginalText = text,
                    Timestamp = DateTime.UtcNow
                },
                StartIndex = 0,
                EndIndex = text.Length
            }
        };
    }

    private VoiceCode.Common.Enums.IntentCategory MapIntentToCategory(string intent)
    {
        return intent switch
        {
            "create_feature" => VoiceCode.Common.Enums.IntentCategory.CodeGeneration,
            "modify_feature" => VoiceCode.Common.Enums.IntentCategory.CodeGeneration,
            "fix_bug" => VoiceCode.Common.Enums.IntentCategory.ErrorFixing,
            "refactor_code" => VoiceCode.Common.Enums.IntentCategory.CodeRefactoring,
            "add_tests" => VoiceCode.Common.Enums.IntentCategory.Testing,
            "documentation" => VoiceCode.Common.Enums.IntentCategory.Documentation,
            _ => VoiceCode.Common.Enums.IntentCategory.General
        };
    }
}