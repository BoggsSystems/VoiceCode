using System.Text.RegularExpressions;

namespace VoiceCode.ClaudeService.Services;

public interface ITokenCounterService
{
    int EstimateTokens(string text);
    bool WillExceedLimit(string text, int maxTokens);
    string TruncateToTokenLimit(string text, int maxTokens);
}

public class TokenCounterService : ITokenCounterService
{
    private readonly ILogger<TokenCounterService> _logger;
    
    // Approximation: 1 token ≈ 4 characters for English text
    // This is a rough estimate; actual tokenization is more complex
    private const double CharactersPerToken = 4.0;

    public TokenCounterService(ILogger<TokenCounterService> logger)
    {
        _logger = logger;
    }

    public int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        // Clean up the text
        text = CleanText(text);

        // Count words and special characters
        var words = CountWords(text);
        var specialChars = CountSpecialCharacters(text);
        
        // Estimate based on characters with adjustments
        var characterCount = text.Length;
        var baseEstimate = (int)Math.Ceiling(characterCount / CharactersPerToken);
        
        // Adjust for code content (tends to have more tokens)
        if (IsLikelyCode(text))
        {
            baseEstimate = (int)(baseEstimate * 1.3);
        }

        // Add tokens for special characters
        baseEstimate += specialChars / 10;

        _logger.LogDebug("Estimated {Tokens} tokens for {Characters} characters", baseEstimate, characterCount);

        return baseEstimate;
    }

    public bool WillExceedLimit(string text, int maxTokens)
    {
        var estimatedTokens = EstimateTokens(text);
        var willExceed = estimatedTokens > maxTokens;
        
        if (willExceed)
        {
            _logger.LogWarning("Text will exceed token limit: {Estimated} > {Max}", estimatedTokens, maxTokens);
        }

        return willExceed;
    }

    public string TruncateToTokenLimit(string text, int maxTokens)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var currentTokens = EstimateTokens(text);
        if (currentTokens <= maxTokens)
            return text;

        // Calculate approximate character limit
        var ratio = (double)maxTokens / currentTokens;
        var targetLength = (int)(text.Length * ratio * 0.9); // 90% to leave some buffer
        
        if (targetLength >= text.Length)
            return text;

        // Try to truncate at a natural boundary
        var truncated = TruncateAtNaturalBoundary(text, targetLength);
        
        // Add ellipsis to indicate truncation
        truncated += "\n... (truncated)";

        _logger.LogInformation("Truncated text from {Original} to {Truncated} characters", 
            text.Length, truncated.Length);

        return truncated;
    }

    private string CleanText(string text)
    {
        // Normalize whitespace
        text = Regex.Replace(text, @"\s+", " ");
        
        // Remove excessive line breaks
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        
        return text.Trim();
    }

    private int CountWords(string text)
    {
        return Regex.Matches(text, @"\b\w+\b").Count;
    }

    private int CountSpecialCharacters(string text)
    {
        return Regex.Matches(text, @"[^a-zA-Z0-9\s]").Count;
    }

    private bool IsLikelyCode(string text)
    {
        // Simple heuristics to detect code
        var codeIndicators = new[]
        {
            @"\bfunction\b", @"\bclass\b", @"\bif\s*\(", @"\bfor\s*\(",
            @"\breturn\b", @"\bvar\b", @"\bconst\b", @"\blet\b",
            @"[{}\[\];]", @"=>", @"\+\+", @"--", @"==", @"!="
        };

        var indicatorCount = codeIndicators.Count(pattern => 
            Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase));

        return indicatorCount >= 3;
    }

    private string TruncateAtNaturalBoundary(string text, int targetLength)
    {
        if (targetLength >= text.Length)
            return text;

        // Try to find a good truncation point
        var truncationPoints = new[]
        {
            text.LastIndexOf("\n\n", targetLength),
            text.LastIndexOf(". ", targetLength),
            text.LastIndexOf("\n", targetLength),
            text.LastIndexOf(", ", targetLength),
            text.LastIndexOf(" ", targetLength)
        };

        var bestPoint = truncationPoints.Where(p => p > targetLength * 0.8 && p < targetLength).Max();
        
        if (bestPoint > 0)
        {
            return text.Substring(0, bestPoint);
        }

        // Fall back to simple truncation
        return text.Substring(0, targetLength);
    }
}