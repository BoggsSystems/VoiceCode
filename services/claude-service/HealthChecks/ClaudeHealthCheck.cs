using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using VoiceCode.ClaudeService.Configuration;

namespace VoiceCode.ClaudeService.HealthChecks;

public class ClaudeHealthCheck : IHealthCheck
{
    private readonly HttpClient _httpClient;
    private readonly ClaudeOptions _options;
    private readonly ILogger<ClaudeHealthCheck> _logger;

    public ClaudeHealthCheck(
        HttpClient httpClient,
        IOptions<ClaudeOptions> options,
        ILogger<ClaudeHealthCheck> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        
        // Configure HTTP client for health checks
        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
        _httpClient.DefaultRequestHeaders.Add("x-api-key", _options.ApiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Make a simple API call to verify connectivity
            // Using a minimal prompt to check API availability
            var testRequest = new
            {
                model = _options.Model,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = "Reply with 'OK'"
                    }
                },
                max_tokens = 10
            };

            var content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(testRequest),
                System.Text.Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync("/v1/messages", content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Claude API health check passed");
                
                return HealthCheckResult.Healthy(
                    "Claude API is healthy",
                    new Dictionary<string, object>
                    {
                        { "endpoint", _options.BaseUrl },
                        { "model", _options.Model },
                        { "status_code", (int)response.StatusCode }
                    });
            }

            _logger.LogWarning("Claude API health check failed: {StatusCode}", response.StatusCode);

            return HealthCheckResult.Unhealthy(
                $"Claude API returned {response.StatusCode}",
                data: new Dictionary<string, object>
                {
                    { "status_code", (int)response.StatusCode },
                    { "reason", response.ReasonPhrase ?? "Unknown" }
                });
        }
        catch (TaskCanceledException)
        {
            _logger.LogError("Claude API health check timed out");
            return HealthCheckResult.Unhealthy("Claude API request timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Claude API health check failed");

            return HealthCheckResult.Unhealthy(
                "Claude API is unhealthy",
                ex,
                new Dictionary<string, object>
                {
                    { "error", ex.Message }
                });
        }
    }
}