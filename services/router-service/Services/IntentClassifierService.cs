using Azure;
using Azure.AI.TextAnalytics;
using Microsoft.Extensions.Options;
using Microsoft.ML;
using System.Text.RegularExpressions;
using VoiceCode.Common.Models;
using VoiceCode.RouterService.Configuration;
using VoiceCode.RouterService.Models;

namespace VoiceCode.RouterService.Services;

public interface IIntentClassifier
{
    Task<Intent> ClassifyIntentAsync(string transcript, UserContext context);
    Task<List<IntentAnalysis>> AnalyzeIntentsAsync(string text, string context);
}

public class IntentClassifierService : IIntentClassifier
{
    private readonly TextAnalyticsClient? _textAnalyticsClient;
    private readonly MLContext _mlContext;
    private readonly IntentClassificationOptions _options;
    private readonly ILogger<IntentClassifierService> _logger;
    private ITransformer? _mlModel;
    private readonly Dictionary<string, IntentPattern> _intentPatterns;

    public IntentClassifierService(
        IOptions<IntentClassificationOptions> options,
        ILogger<IntentClassifierService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _mlContext = new MLContext(seed: 0);
        
        if (!string.IsNullOrEmpty(_options.TextAnalyticsEndpoint))
        {
            _textAnalyticsClient = new TextAnalyticsClient(
                new Uri(_options.TextAnalyticsEndpoint),
                new AzureKeyCredential(_options.TextAnalyticsKey));
        }

        _intentPatterns = InitializeIntentPatterns();
        LoadMLModel();
    }

    public async Task<Intent> ClassifyIntentAsync(string transcript, UserContext context)
    {
        try
        {
            var intent = new Intent
            {
                OriginalText = transcript,
                Timestamp = DateTime.UtcNow
            };

            // Try ML model first if available
            if (_options.UseMLModel && _mlModel != null)
            {
                var mlIntent = ClassifyWithMLModel(transcript);
                if (mlIntent.Confidence >= _options.ConfidenceThreshold)
                {
                    intent = mlIntent;
                    _logger.LogDebug("ML model classified intent as {Type} with confidence {Confidence}",
                        intent.Type, intent.Confidence);
                    return intent;
                }
            }

            // Try pattern matching
            var patternIntent = ClassifyWithPatterns(transcript);
            if (patternIntent.Confidence >= _options.ConfidenceThreshold)
            {
                intent = patternIntent;
                _logger.LogDebug("Pattern matching classified intent as {Type} with confidence {Confidence}",
                    intent.Type, intent.Confidence);
                return intent;
            }

            // Try Azure Text Analytics if available
            if (_textAnalyticsClient != null)
            {
                var textIntent = await ClassifyWithTextAnalyticsAsync(transcript);
                if (textIntent.Confidence >= _options.ConfidenceThreshold)
                {
                    intent = textIntent;
                    _logger.LogDebug("Text Analytics classified intent as {Type} with confidence {Confidence}",
                        intent.Type, intent.Confidence);
                    return intent;
                }
            }

            // Fallback classification based on keywords
            if (_options.EnableFallbackClassification)
            {
                intent = FallbackClassification(transcript);
                _logger.LogDebug("Fallback classification resulted in {Type} with confidence {Confidence}",
                    intent.Type, intent.Confidence);
            }

            return intent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Intent classification failed");
            return new Intent
            {
                Type = "unknown",
                Category = IntentCategory.Unknown,
                Confidence = 0.0,
                OriginalText = transcript
            };
        }
    }

    public async Task<List<IntentAnalysis>> AnalyzeIntentsAsync(string text, string context)
    {
        var analyses = new List<IntentAnalysis>();

        // Split text into sentences for more granular analysis
        var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var sentence in sentences)
        {
            var trimmedSentence = sentence.Trim();
            if (string.IsNullOrWhiteSpace(trimmedSentence))
                continue;

            var intent = await ClassifyIntentAsync(trimmedSentence, new UserContext());
            
            analyses.Add(new IntentAnalysis
            {
                Text = trimmedSentence,
                Intent = intent,
                StartIndex = text.IndexOf(trimmedSentence),
                EndIndex = text.IndexOf(trimmedSentence) + trimmedSentence.Length
            });
        }

        return analyses;
    }

    private Intent ClassifyWithMLModel(string transcript)
    {
        if (_mlModel == null)
        {
            return new Intent { Type = "unknown", Confidence = 0.0 };
        }

        try
        {
            var predictionEngine = _mlContext.Model.CreatePredictionEngine<IntentInput, IntentPrediction>(_mlModel);
            var input = new IntentInput { Text = transcript };
            var prediction = predictionEngine.Predict(input);

            return new Intent
            {
                Type = prediction.PredictedLabel ?? "unknown",
                Category = MapTypeToCategory(prediction.PredictedLabel ?? "unknown"),
                Confidence = prediction.Score?.Max() ?? 0.0,
                OriginalText = transcript
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ML model prediction failed");
            return new Intent { Type = "unknown", Confidence = 0.0 };
        }
    }

    private Intent ClassifyWithPatterns(string transcript)
    {
        var normalizedText = transcript.ToLowerInvariant();
        Intent? bestMatch = null;
        double highestScore = 0.0;

        foreach (var pattern in _intentPatterns)
        {
            var score = pattern.Value.CalculateScore(normalizedText);
            if (score > highestScore)
            {
                highestScore = score;
                bestMatch = new Intent
                {
                    Type = pattern.Key,
                    Category = pattern.Value.Category,
                    Confidence = score,
                    OriginalText = transcript
                };
            }
        }

        return bestMatch ?? new Intent { Type = "unknown", Confidence = 0.0 };
    }

    private async Task<Intent> ClassifyWithTextAnalyticsAsync(string transcript)
    {
        if (_textAnalyticsClient == null)
        {
            return new Intent { Type = "unknown", Confidence = 0.0 };
        }

        try
        {
            // Use key phrase extraction and sentiment analysis to infer intent
            var keyPhrases = await _textAnalyticsClient.ExtractKeyPhrasesAsync(transcript);
            var sentiment = await _textAnalyticsClient.AnalyzeSentimentAsync(transcript);

            // Analyze key phrases to determine intent
            var intent = AnalyzeKeyPhrasesForIntent(keyPhrases.Value.ToList(), sentiment.Value);
            intent.OriginalText = transcript;

            return intent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Text Analytics classification failed");
            return new Intent { Type = "unknown", Confidence = 0.0 };
        }
    }

    private Intent FallbackClassification(string transcript)
    {
        var normalizedText = transcript.ToLowerInvariant();

        // Simple keyword-based classification
        if (ContainsAny(normalizedText, "create", "generate", "make", "build", "write", "implement"))
        {
            return new Intent
            {
                Type = "generate_code",
                Category = IntentCategory.CodeGeneration,
                Confidence = 0.6,
                OriginalText = transcript
            };
        }

        if (ContainsAny(normalizedText, "explain", "what", "how", "why", "understand"))
        {
            return new Intent
            {
                Type = "explain_code",
                Category = IntentCategory.CodeExplanation,
                Confidence = 0.6,
                OriginalText = transcript
            };
        }

        if (ContainsAny(normalizedText, "fix", "error", "bug", "issue", "problem", "wrong"))
        {
            return new Intent
            {
                Type = "fix_error",
                Category = IntentCategory.ErrorFixing,
                Confidence = 0.6,
                OriginalText = transcript
            };
        }

        if (ContainsAny(normalizedText, "refactor", "improve", "optimize", "clean"))
        {
            return new Intent
            {
                Type = "refactor_code",
                Category = IntentCategory.CodeRefactoring,
                Confidence = 0.6,
                OriginalText = transcript
            };
        }

        if (ContainsAny(normalizedText, "test", "unit test", "integration test"))
        {
            return new Intent
            {
                Type = "create_tests",
                Category = IntentCategory.Testing,
                Confidence = 0.6,
                OriginalText = transcript
            };
        }

        return new Intent
        {
            Type = "general",
            Category = IntentCategory.General,
            Confidence = 0.5,
            OriginalText = transcript
        };
    }

    private Dictionary<string, IntentPattern> InitializeIntentPatterns()
    {
        return new Dictionary<string, IntentPattern>
        {
            ["generate_code"] = new IntentPattern
            {
                Category = IntentCategory.CodeGeneration,
                RequiredKeywords = new[] { "create", "generate", "make", "build", "implement", "write" },
                OptionalKeywords = new[] { "function", "class", "method", "component", "service", "api" },
                NegativeKeywords = new[] { "don't", "not", "without" },
                Patterns = new[]
                {
                    @"(create|generate|make|build|write|implement)\s+(?:a\s+)?(\w+)",
                    @"(?:can you|could you|please)?\s*(create|generate|make|build)",
                    @"(?:i need|i want)\s+(?:a\s+)?(?:new\s+)?(\w+)"
                }
            },
            ["explain_code"] = new IntentPattern
            {
                Category = IntentCategory.CodeExplanation,
                RequiredKeywords = new[] { "explain", "what", "how", "why", "understand", "tell" },
                OptionalKeywords = new[] { "does", "work", "mean", "this", "code" },
                Patterns = new[]
                {
                    @"(explain|what|how)\s+(?:does\s+)?(?:this\s+)?(\w+)",
                    @"(?:can you|could you)?\s*(explain|tell)\s+(?:me\s+)?(?:about|what)"
                }
            },
            ["fix_error"] = new IntentPattern
            {
                Category = IntentCategory.ErrorFixing,
                RequiredKeywords = new[] { "fix", "error", "bug", "issue", "problem", "wrong", "broken" },
                OptionalKeywords = new[] { "debug", "solve", "resolve", "help" },
                Patterns = new[]
                {
                    @"(fix|solve|resolve|debug)\s+(?:this\s+)?(?:error|bug|issue|problem)",
                    @"(?:there's|there is|i have)\s+(?:an?\s+)?(error|bug|issue|problem)"
                }
            },
            ["refactor_code"] = new IntentPattern
            {
                Category = IntentCategory.CodeRefactoring,
                RequiredKeywords = new[] { "refactor", "improve", "optimize", "clean", "better" },
                OptionalKeywords = new[] { "performance", "readable", "maintainable", "efficient" },
                Patterns = new[]
                {
                    @"(refactor|improve|optimize|clean)\s+(?:this\s+)?(?:code|function|method)",
                    @"make\s+(?:this\s+)?(?:code\s+)?(?:more\s+)?(readable|efficient|better)"
                }
            }
        };
    }

    private IntentCategory MapTypeToCategory(string type)
    {
        return type switch
        {
            "generate_code" => IntentCategory.CodeGeneration,
            "explain_code" => IntentCategory.CodeExplanation,
            "fix_error" => IntentCategory.ErrorFixing,
            "refactor_code" => IntentCategory.CodeRefactoring,
            "create_tests" => IntentCategory.Testing,
            "document_code" => IntentCategory.Documentation,
            "manage_project" => IntentCategory.ProjectManagement,
            "system_command" => IntentCategory.SystemCommand,
            _ => IntentCategory.General
        };
    }

    private Intent AnalyzeKeyPhrasesForIntent(List<string> keyPhrases, DocumentSentiment sentiment)
    {
        // Analyze key phrases to determine intent type
        var phrases = string.Join(" ", keyPhrases).ToLowerInvariant();

        foreach (var pattern in _intentPatterns)
        {
            var score = 0.0;
            var requiredCount = pattern.Value.RequiredKeywords.Count(k => phrases.Contains(k));
            var optionalCount = pattern.Value.OptionalKeywords.Count(k => phrases.Contains(k));
            
            if (requiredCount > 0)
            {
                score = (requiredCount / (double)pattern.Value.RequiredKeywords.Length) * 0.7 +
                       (optionalCount / (double)pattern.Value.OptionalKeywords.Length) * 0.3;

                if (score >= 0.5)
                {
                    return new Intent
                    {
                        Type = pattern.Key,
                        Category = pattern.Value.Category,
                        Confidence = score
                    };
                }
            }
        }

        return new Intent { Type = "general", Category = IntentCategory.General, Confidence = 0.5 };
    }

    private bool ContainsAny(string text, params string[] keywords)
    {
        return keywords.Any(keyword => text.Contains(keyword));
    }

    private void LoadMLModel()
    {
        if (!_options.UseMLModel || string.IsNullOrEmpty(_options.ModelPath))
            return;

        try
        {
            if (File.Exists(_options.ModelPath))
            {
                _mlModel = _mlContext.Model.Load(_options.ModelPath, out _);
                _logger.LogInformation("Loaded ML model from {Path}", _options.ModelPath);
            }
            else
            {
                _logger.LogWarning("ML model file not found at {Path}", _options.ModelPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load ML model");
        }
    }
}

public class IntentPattern
{
    public IntentCategory Category { get; set; }
    public string[] RequiredKeywords { get; set; } = Array.Empty<string>();
    public string[] OptionalKeywords { get; set; } = Array.Empty<string>();
    public string[] NegativeKeywords { get; set; } = Array.Empty<string>();
    public string[] Patterns { get; set; } = Array.Empty<string>();

    public double CalculateScore(string text)
    {
        // Check for negative keywords
        if (NegativeKeywords.Any(k => text.Contains(k)))
            return 0.0;

        var score = 0.0;
        
        // Check required keywords
        var requiredMatches = RequiredKeywords.Count(k => text.Contains(k));
        if (requiredMatches == 0)
            return 0.0;
        
        score += (requiredMatches / (double)RequiredKeywords.Length) * 0.5;

        // Check optional keywords
        var optionalMatches = OptionalKeywords.Count(k => text.Contains(k));
        score += (optionalMatches / (double)Math.Max(OptionalKeywords.Length, 1)) * 0.3;

        // Check patterns
        var patternMatches = Patterns.Count(p => Regex.IsMatch(text, p, RegexOptions.IgnoreCase));
        score += (patternMatches / (double)Math.Max(Patterns.Length, 1)) * 0.2;

        return Math.Min(score, 1.0);
    }
}