using Microsoft.Extensions.Options;
using VoiceCode.Common.Interfaces;
using VoiceCode.Common.Models;
using VoiceCode.RouterService.Configuration;

namespace VoiceCode.RouterService.Services;

public interface IContextManager
{
    Task<UserContext> GetContextAsync(string userId, string sessionId);
    Task UpdateContextAsync(UserContext context, string transcript, Intent intent);
    Task ClearContextAsync(string userId, string sessionId);
}

public class ContextManagerService : IContextManager
{
    private readonly ICacheService _cache;
    private readonly RouterOptions _options;
    private readonly ILogger<ContextManagerService> _logger;

    public ContextManagerService(
        ICacheService cache,
        IOptions<RouterOptions> options,
        ILogger<ContextManagerService> logger)
    {
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<UserContext> GetContextAsync(string userId, string sessionId)
    {
        var cacheKey = GetCacheKey(userId, sessionId);
        var context = await _cache.GetAsync<UserContext>(cacheKey);

        if (context == null)
        {
            _logger.LogDebug("Creating new context for user {UserId}, session {SessionId}", userId, sessionId);
            context = new UserContext
            {
                UserId = userId,
                SessionId = sessionId,
                RecentInteractions = new List<ContextEntry>(),
                Preferences = await GetUserPreferencesAsync(userId),
                SessionData = new Dictionary<string, object>(),
                LastUpdated = DateTime.UtcNow
            };

            await _cache.SetAsync(cacheKey, context, _options.ContextTimeout);
        }
        else
        {
            _logger.LogDebug("Retrieved existing context for user {UserId}, session {SessionId}", userId, sessionId);
        }

        return context;
    }

    public async Task UpdateContextAsync(UserContext context, string transcript, Intent intent)
    {
        try
        {
            var entry = new ContextEntry
            {
                Id = Guid.NewGuid().ToString(),
                Transcript = transcript,
                Intent = intent,
                Timestamp = DateTime.UtcNow
            };

            // Add to recent interactions
            context.RecentInteractions.Insert(0, entry);

            // Maintain history size limit
            if (context.RecentInteractions.Count > _options.MaxContextHistoryItems)
            {
                context.RecentInteractions = context.RecentInteractions
                    .Take(_options.MaxContextHistoryItems)
                    .ToList();
            }

            // Update session data based on intent
            UpdateSessionData(context, intent);

            context.LastUpdated = DateTime.UtcNow;

            // Save to cache
            var cacheKey = GetCacheKey(context.UserId, context.SessionId);
            await _cache.SetAsync(cacheKey, context, _options.ContextTimeout);

            _logger.LogDebug("Updated context for user {UserId} with intent {IntentType}",
                context.UserId, intent.Type);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update context");
        }
    }

    public async Task ClearContextAsync(string userId, string sessionId)
    {
        var cacheKey = GetCacheKey(userId, sessionId);
        await _cache.DeleteAsync(cacheKey);
        _logger.LogInformation("Cleared context for user {UserId}, session {SessionId}", userId, sessionId);
    }

    private void UpdateSessionData(UserContext context, Intent intent)
    {
        // Track current working context
        if (intent.Category == IntentCategory.CodeGeneration)
        {
            if (intent.Entities.Any())
            {
                context.SessionData["current_component"] = intent.Entities.First();
            }

            if (intent.Parameters.TryGetValue("language", out var language))
            {
                context.SessionData["current_language"] = language;
            }
        }

        // Track error context
        if (intent.Category == IntentCategory.ErrorFixing)
        {
            context.SessionData["has_active_error"] = true;
            if (intent.Parameters.TryGetValue("error_type", out var errorType))
            {
                context.SessionData["error_type"] = errorType;
            }
        }

        // Track project context
        if (intent.Category == IntentCategory.ProjectManagement)
        {
            if (intent.Parameters.TryGetValue("project_name", out var projectName))
            {
                context.SessionData["current_project"] = projectName;
            }
        }

        // Update interaction statistics
        var stats = context.SessionData.GetValueOrDefault("interaction_stats") as Dictionary<string, int> 
                    ?? new Dictionary<string, int>();
        
        var categoryKey = intent.Category.ToString();
        stats[categoryKey] = stats.GetValueOrDefault(categoryKey, 0) + 1;
        context.SessionData["interaction_stats"] = stats;
    }

    private async Task<UserPreferences> GetUserPreferencesAsync(string userId)
    {
        // Try to get from cache
        var preferencesCacheKey = $"user_preferences:{userId}";
        var preferences = await _cache.GetAsync<UserPreferences>(preferencesCacheKey);

        if (preferences != null)
        {
            return preferences;
        }

        // Return default preferences
        return new UserPreferences
        {
            PreferredLanguage = "csharp",
            CodingStyle = "clean-code",
            IndentationStyle = "spaces",
            IndentationSize = 4,
            PreferExplicitTypes = true,
            PreferAsyncMethods = true
        };
    }

    private string GetCacheKey(string userId, string sessionId)
    {
        return $"context:{userId}:{sessionId}";
    }
}