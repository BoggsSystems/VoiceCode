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
    public int Worker { get; set; }
    public string Instructions { get; set; } = string.Empty;
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

            _logger.LogInformation("OpenAI response content: {Content}", content);

            var classification = JsonSerializer.Deserialize<IntentClassification>(content, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });
            
            if (classification == null)
            {
                _logger.LogWarning("Failed to deserialize OpenAI response: {Content}", content);
                return CreateFallbackClassification(transcript);
            }

            _logger.LogInformation("Command classified for Worker {Worker} with instructions: {Instructions}", 
                classification.Worker, classification.Instructions);

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
        return @"You are a voice command parser for a development system.

The user will give a voice command that starts with a worker identifier followed by instructions. The worker can be specified as:
- ""Worker 1"", ""Worker 2"", etc. (with numbers)
- ""Worker one"", ""Worker two"", etc. (with words)

Extract:
1. The worker number as an integer (convert ""one"" to 1, ""two"" to 2, etc.)
2. The instructions (everything after the worker identifier)

Examples:
- Input: ""Worker 1, create a login page with email and password fields""
  Output: {""worker"": 1, ""instructions"": ""create a login page with email and password fields""}
  
- Input: ""Worker one create the scaffolding for react application""
  Output: {""worker"": 1, ""instructions"": ""create the scaffolding for react application""}
  
- Input: ""Worker two, fix the navigation bug in the header""
  Output: {""worker"": 2, ""instructions"": ""fix the navigation bug in the header""}

If no worker is specified, default to worker 1.
Remove any trailing punctuation from instructions.

Respond ONLY with a valid JSON object:
{
  ""worker"": <number>,
  ""instructions"": ""<the task instructions>""
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
        // Simple fallback - try to extract worker number
        var lowerTranscript = transcript.ToLowerInvariant();
        var workerNumber = 1; // default
        
        // Try to find "worker X" pattern with numbers
        var match = System.Text.RegularExpressions.Regex.Match(lowerTranscript, @"worker\s*(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var number))
        {
            workerNumber = number;
        }
        else
        {
            // Try to find "worker one/two/three" pattern
            var wordMatch = System.Text.RegularExpressions.Regex.Match(lowerTranscript, @"worker\s*(one|two|three|four|five|six|seven|eight|nine|ten)");
            if (wordMatch.Success)
            {
                workerNumber = wordMatch.Groups[1].Value switch
                {
                    "one" => 1,
                    "two" => 2,
                    "three" => 3,
                    "four" => 4,
                    "five" => 5,
                    "six" => 6,
                    "seven" => 7,
                    "eight" => 8,
                    "nine" => 9,
                    "ten" => 10,
                    _ => 1
                };
                match = wordMatch;
            }
        }
        
        // Remove the worker part to get instructions
        var instructions = transcript;
        if (match.Success)
        {
            instructions = transcript.Substring(match.Index + match.Length).Trim(' ', ',', '.');
        }

        _logger.LogInformation("Fallback classification used - Worker: {Worker}, Instructions: {Instructions}", 
            workerNumber, instructions);

        return new IntentClassification
        {
            Worker = workerNumber,
            Instructions = string.IsNullOrWhiteSpace(instructions) ? transcript : instructions
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
            Type = $"worker_{classification.Worker}",
            Category = VoiceCode.Common.Enums.IntentCategory.General,
            Confidence = 1.0,
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
                    Type = $"worker_{classification.Worker}",
                    Category = VoiceCode.Common.Enums.IntentCategory.General,
                    Confidence = 1.0,
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