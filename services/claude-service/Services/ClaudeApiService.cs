using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using VoiceCode.ClaudeService.Configuration;
using VoiceCode.Common.DTOs;
using VoiceCode.Common.Interfaces;
using Polly;
using Polly.Retry;

namespace VoiceCode.ClaudeService.Services;

public class ClaudeApiService : IClaudeService
{
    private readonly HttpClient _httpClient;
    private readonly ClaudeOptions _options;
    private readonly ILogger<ClaudeApiService> _logger;
    private readonly ICacheService _cache;
    private readonly IPromptTemplateService _promptTemplates;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;

    public ClaudeApiService(
        HttpClient httpClient,
        IOptions<ClaudeOptions> options,
        ILogger<ClaudeApiService> logger,
        ICacheService cache,
        IPromptTemplateService promptTemplates)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _cache = cache;
        _promptTemplates = promptTemplates;

        // Configure HTTP client
        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
        _httpClient.DefaultRequestHeaders.Add("x-api-key", _options.ApiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.Timeout = _options.Timeout;

        // Configure retry policy
        _retryPolicy = Policy
            .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
            .Or<HttpRequestException>()
            .Or<TaskCanceledException>()
            .WaitAndRetryAsync(
                _options.MaxRetries,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    _logger.LogWarning("Retry {RetryCount} after {Delay}ms", retryCount, timespan.TotalMilliseconds);
                });
    }

    public async Task<CodeGenerationResponse> GenerateCodeAsync(CodeGenerationRequest request)
    {
        using var activity = System.Diagnostics.Activity.Current;
        activity?.SetTag("claude.request_type", request.Type);
        activity?.SetTag("claude.language", request.Language);

        try
        {
            // Check cache for similar requests
            var cacheKey = GenerateCacheKey(request);
            var cachedResponse = await _cache.GetAsync<CodeGenerationResponse>(cacheKey);
            if (cachedResponse != null)
            {
                _logger.LogInformation("Returning cached response for request type: {Type}", request.Type);
                return cachedResponse;
            }

            // Build the prompt
            var prompt = await _promptTemplates.BuildPromptAsync(request);

            // Create the API request
            var apiRequest = new
            {
                model = _options.Model,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = prompt
                    }
                },
                max_tokens = _options.MaxTokens,
                temperature = _options.Temperature,
                system = await _promptTemplates.GetSystemPromptAsync(request.Type)
            };

            var json = JsonSerializer.Serialize(apiRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Make the API call with retry
            var response = await _retryPolicy.ExecuteAsync(async () =>
                await _httpClient.PostAsync("/v1/messages", content));

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("Claude API error: {StatusCode} - {Error}", response.StatusCode, error);
                throw new InvalidOperationException($"Claude API error: {response.StatusCode}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var apiResponse = JsonSerializer.Deserialize<ClaudeApiResponse>(responseJson);

            if (apiResponse?.Content == null || apiResponse.Content.Count == 0)
            {
                throw new InvalidOperationException("Empty response from Claude API");
            }

            var responseText = string.Join("\n", apiResponse.Content.Select(c => c.Text));

            var result = new CodeGenerationResponse
            {
                Id = Guid.NewGuid().ToString(),
                Code = ExtractCode(responseText),
                Explanation = ExtractExplanation(responseText),
                Language = request.Language,
                Tokens = apiResponse.Usage?.OutputTokens ?? 0,
                Model = _options.Model,
                ProcessingTime = DateTime.UtcNow,
                RawResponse = responseText
            };

            // Cache the response
            await _cache.SetAsync(cacheKey, result, TimeSpan.FromHours(1));

            _logger.LogInformation("Code generation completed: {Tokens} tokens used", result.Tokens);
            activity?.SetTag("claude.tokens_used", result.Tokens);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Code generation failed");
            activity?.SetTag("claude.error", ex.Message);
            throw;
        }
    }

    public async Task<string> ExplainCodeAsync(string code, string language)
    {
        var request = new CodeGenerationRequest
        {
            Type = "explain",
            Code = code,
            Language = language,
            Context = new Dictionary<string, object>
            {
                { "operation", "explain" }
            }
        };

        var response = await GenerateCodeAsync(request);
        return response.Explanation;
    }

    public async Task<string> FixCodeAsync(string code, string error, string language)
    {
        var request = new CodeGenerationRequest
        {
            Type = "fix",
            Code = code,
            Language = language,
            Context = new Dictionary<string, object>
            {
                { "error", error },
                { "operation", "fix" }
            }
        };

        var response = await GenerateCodeAsync(request);
        return response.Code;
    }

    public async Task<string> RefactorCodeAsync(string code, string instruction, string language)
    {
        var request = new CodeGenerationRequest
        {
            Type = "refactor",
            Code = code,
            Language = language,
            Instructions = instruction,
            Context = new Dictionary<string, object>
            {
                { "operation", "refactor" },
                { "instruction", instruction }
            }
        };

        var response = await GenerateCodeAsync(request);
        return response.Code;
    }

    private string GenerateCacheKey(CodeGenerationRequest request)
    {
        var key = $"claude:{request.Type}:{request.Language}";
        
        if (!string.IsNullOrEmpty(request.Code))
        {
            var codeHash = Convert.ToBase64String(
                System.Security.Cryptography.SHA256.HashData(
                    Encoding.UTF8.GetBytes(request.Code)));
            key += $":{codeHash.Substring(0, 8)}";
        }

        if (!string.IsNullOrEmpty(request.Instructions))
        {
            var instructionHash = Convert.ToBase64String(
                System.Security.Cryptography.SHA256.HashData(
                    Encoding.UTF8.GetBytes(request.Instructions)));
            key += $":{instructionHash.Substring(0, 8)}";
        }

        return key;
    }

    private string ExtractCode(string response)
    {
        // Extract code blocks from the response
        var codeBlocks = new List<string>();
        var lines = response.Split('\n');
        var inCodeBlock = false;
        var currentBlock = new StringBuilder();

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```"))
            {
                if (inCodeBlock)
                {
                    codeBlocks.Add(currentBlock.ToString());
                    currentBlock.Clear();
                }
                inCodeBlock = !inCodeBlock;
                continue;
            }

            if (inCodeBlock)
            {
                currentBlock.AppendLine(line);
            }
        }

        return codeBlocks.Count > 0 ? string.Join("\n\n", codeBlocks) : response;
    }

    private string ExtractExplanation(string response)
    {
        // Extract non-code explanation from the response
        var lines = response.Split('\n');
        var explanation = new StringBuilder();
        var inCodeBlock = false;

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```"))
            {
                inCodeBlock = !inCodeBlock;
                continue;
            }

            if (!inCodeBlock && !string.IsNullOrWhiteSpace(line))
            {
                explanation.AppendLine(line);
            }
        }

        return explanation.ToString().Trim();
    }
}

// Response models
public class ClaudeApiResponse
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public List<ContentBlock> Content { get; set; } = new();
    public string Model { get; set; } = string.Empty;
    public Usage? Usage { get; set; }
}

public class ContentBlock
{
    public string Type { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

public class Usage
{
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
}