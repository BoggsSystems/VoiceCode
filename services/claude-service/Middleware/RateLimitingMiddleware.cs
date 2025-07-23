using System.Net;
using VoiceCode.Common.Interfaces;

namespace VoiceCode.ClaudeService.Middleware;

public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ICacheService _cache;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private readonly RateLimitOptions _options;

    public RateLimitingMiddleware(
        RequestDelegate next,
        ICacheService cache,
        ILogger<RateLimitingMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next;
        _cache = cache;
        _logger = logger;
        _options = configuration.GetSection("RateLimit").Get<RateLimitOptions>() ?? new RateLimitOptions();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled)
        {
            await _next(context);
            return;
        }

        var userId = context.User?.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        var endpoint = $"{context.Request.Method}:{context.Request.Path}";
        
        // Check rate limits
        var isAllowed = await CheckRateLimitAsync(userId, endpoint);
        
        if (!isAllowed)
        {
            _logger.LogWarning("Rate limit exceeded for user {UserId} on endpoint {Endpoint}", userId, endpoint);
            
            context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
            context.Response.Headers.Add("Retry-After", _options.WindowSeconds.ToString());
            
            await context.Response.WriteAsync("Rate limit exceeded. Please try again later.");
            return;
        }

        await _next(context);
    }

    private async Task<bool> CheckRateLimitAsync(string userId, string endpoint)
    {
        // Different limits for different endpoints
        var limit = GetLimitForEndpoint(endpoint);
        var window = TimeSpan.FromSeconds(_options.WindowSeconds);
        
        var key = $"ratelimit:{userId}:{endpoint}";
        var count = await _cache.IncrementAsync(key, expiry: window);
        
        if (count > limit)
        {
            // Add to blocked list temporarily
            var blockKey = $"ratelimit:blocked:{userId}";
            await _cache.SetAsync(blockKey, "blocked", TimeSpan.FromMinutes(5));
            return false;
        }

        // Check if user is temporarily blocked
        var isBlocked = await _cache.ExistsAsync($"ratelimit:blocked:{userId}");
        return !isBlocked;
    }

    private int GetLimitForEndpoint(string endpoint)
    {
        // Different limits for different operations
        if (endpoint.Contains("/generate", StringComparison.OrdinalIgnoreCase))
            return _options.GenerateLimit;
        
        if (endpoint.Contains("/explain", StringComparison.OrdinalIgnoreCase))
            return _options.ExplainLimit;
        
        if (endpoint.Contains("/fix", StringComparison.OrdinalIgnoreCase))
            return _options.FixLimit;
        
        if (endpoint.Contains("/refactor", StringComparison.OrdinalIgnoreCase))
            return _options.RefactorLimit;
        
        return _options.DefaultLimit;
    }
}

public class RateLimitOptions
{
    public bool Enabled { get; set; } = true;
    public int WindowSeconds { get; set; } = 60;
    public int DefaultLimit { get; set; } = 100;
    public int GenerateLimit { get; set; } = 20;
    public int ExplainLimit { get; set; } = 50;
    public int FixLimit { get; set; } = 30;
    public int RefactorLimit { get; set; } = 30;
}