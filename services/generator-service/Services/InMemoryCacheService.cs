using System.Collections.Concurrent;
using System.Text.Json;
using VoiceCode.Common.Interfaces;

namespace VoiceCode.GeneratorService.Services;

public class InMemoryCacheService : ICacheService
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly ILogger<InMemoryCacheService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Timer _cleanupTimer;

    private class CacheEntry
    {
        public string Value { get; set; } = string.Empty;
        public DateTime? ExpiresAt { get; set; }
    }

    public InMemoryCacheService(ILogger<InMemoryCacheService> logger)
    {
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        
        // Run cleanup every minute
        _cleanupTimer = new Timer(CleanupExpiredEntries, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        _logger.LogInformation("Using in-memory cache service");
    }

    private void CleanupExpiredEntries(object? state)
    {
        var expiredKeys = _cache
            .Where(kvp => kvp.Value.ExpiresAt.HasValue && kvp.Value.ExpiresAt.Value < DateTime.UtcNow)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _cache.TryRemove(key, out _);
        }

        if (expiredKeys.Any())
        {
            _logger.LogDebug("Cleaned up {Count} expired cache entries", expiredKeys.Count);
        }
    }

    public Task<T?> GetAsync<T>(string key) where T : class
    {
        try
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value < DateTime.UtcNow)
                {
                    _cache.TryRemove(key, out _);
                    _logger.LogDebug("Cache miss for key: {Key} (expired)", key);
                    return Task.FromResult<T?>(null);
                }

                _logger.LogDebug("Cache hit for key: {Key}", key);
                return Task.FromResult(JsonSerializer.Deserialize<T>(entry.Value, _jsonOptions));
            }

            _logger.LogDebug("Cache miss for key: {Key}", key);
            return Task.FromResult<T?>(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving cache key: {Key}", key);
            return Task.FromResult<T?>(null);
        }
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null) where T : class
    {
        try
        {
            var json = JsonSerializer.Serialize(value, _jsonOptions);
            var entry = new CacheEntry
            {
                Value = json,
                ExpiresAt = expiry.HasValue ? DateTime.UtcNow.Add(expiry.Value) : null
            };

            _cache[key] = entry;
            _logger.LogDebug("Cache set for key: {Key}, expiry: {Expiry}", key, expiry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting cache key: {Key}", key);
        }

        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string key)
    {
        try
        {
            var result = _cache.TryRemove(key, out _);
            _logger.LogDebug("Cache delete for key: {Key}, result: {Result}", key, result);
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting cache key: {Key}", key);
            return Task.FromResult(false);
        }
    }

    public Task RemoveAsync(string key)
    {
        return DeleteAsync(key);
    }

    public Task<bool> ExistsAsync(string key)
    {
        try
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value < DateTime.UtcNow)
                {
                    _cache.TryRemove(key, out _);
                    return Task.FromResult(false);
                }
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking cache key existence: {Key}", key);
            return Task.FromResult(false);
        }
    }

    public Task<List<string>> GetKeysAsync(string pattern)
    {
        try
        {
            // Simple pattern matching (supports * wildcard)
            var regex = new System.Text.RegularExpressions.Regex(
                "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$");

            var keys = _cache.Keys
                .Where(key => regex.IsMatch(key))
                .ToList();

            return Task.FromResult(keys);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting keys with pattern: {Pattern}", pattern);
            return Task.FromResult(new List<string>());
        }
    }

    public Task<long> IncrementAsync(string key, long value = 1, TimeSpan? expiry = null)
    {
        try
        {
            var newValue = 0L;
            _cache.AddOrUpdate(key,
                k => new CacheEntry 
                { 
                    Value = value.ToString(), 
                    ExpiresAt = expiry.HasValue ? DateTime.UtcNow.Add(expiry.Value) : null 
                },
                (k, existing) =>
                {
                    if (long.TryParse(existing.Value, out var currentValue))
                    {
                        newValue = currentValue + value;
                        existing.Value = newValue.ToString();
                        if (expiry.HasValue)
                        {
                            existing.ExpiresAt = DateTime.UtcNow.Add(expiry.Value);
                        }
                    }
                    else
                    {
                        newValue = value;
                        existing.Value = value.ToString();
                    }
                    return existing;
                });

            return Task.FromResult(newValue == 0 ? value : newValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error incrementing cache key: {Key}", key);
            throw;
        }
    }

    public Task<bool> SetAddAsync(string key, string value, TimeSpan? expiry = null)
    {
        try
        {
            var added = false;
            _cache.AddOrUpdate(key,
                k =>
                {
                    added = true;
                    return new CacheEntry
                    {
                        Value = JsonSerializer.Serialize(new HashSet<string> { value }, _jsonOptions),
                        ExpiresAt = expiry.HasValue ? DateTime.UtcNow.Add(expiry.Value) : null
                    };
                },
                (k, existing) =>
                {
                    var set = JsonSerializer.Deserialize<HashSet<string>>(existing.Value, _jsonOptions) ?? new HashSet<string>();
                    added = set.Add(value);
                    existing.Value = JsonSerializer.Serialize(set, _jsonOptions);
                    if (expiry.HasValue)
                    {
                        existing.ExpiresAt = DateTime.UtcNow.Add(expiry.Value);
                    }
                    return existing;
                });

            return Task.FromResult(added);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding to set cache key: {Key}", key);
            return Task.FromResult(false);
        }
    }

    public Task<string[]> SetMembersAsync(string key)
    {
        try
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value < DateTime.UtcNow)
                {
                    _cache.TryRemove(key, out _);
                    return Task.FromResult(Array.Empty<string>());
                }

                var set = JsonSerializer.Deserialize<HashSet<string>>(entry.Value, _jsonOptions) ?? new HashSet<string>();
                return Task.FromResult(set.ToArray());
            }

            return Task.FromResult(Array.Empty<string>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting set members for key: {Key}", key);
            return Task.FromResult(Array.Empty<string>());
        }
    }

    public Task<bool> LockAsync(string key, TimeSpan duration)
    {
        var lockValue = Guid.NewGuid().ToString();
        return LockAsync(key, lockValue, duration);
    }

    public Task<bool> LockAsync(string key, string value, TimeSpan expiry)
    {
        try
        {
            var lockKey = $"lock:{key}";
            var added = _cache.TryAdd(lockKey, new CacheEntry
            {
                Value = value,
                ExpiresAt = DateTime.UtcNow.Add(expiry)
            });

            return Task.FromResult(added);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acquiring lock for key: {Key}", key);
            return Task.FromResult(false);
        }
    }

    public Task<bool> UnlockAsync(string key, string value)
    {
        try
        {
            var lockKey = $"lock:{key}";
            if (_cache.TryGetValue(lockKey, out var entry) && entry.Value == value)
            {
                return Task.FromResult(_cache.TryRemove(lockKey, out _));
            }
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing lock for key: {Key}", key);
            return Task.FromResult(false);
        }
    }

    public Task UnlockAsync(string key)
    {
        var lockKey = $"lock:{key}";
        _cache.TryRemove(lockKey, out _);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}