using StackExchange.Redis;
using System.Text.Json;
using VoiceCode.Common.Interfaces;

namespace VoiceCode.RouterService.Services;

public class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _database;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public RedisCacheService(
        IConnectionMultiplexer redis,
        ILogger<RedisCacheService> logger)
    {
        _redis = redis;
        _database = _redis.GetDatabase();
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    public async Task<T?> GetAsync<T>(string key) where T : class
    {
        try
        {
            var value = await _database.StringGetAsync(key);
            
            if (value.IsNullOrEmpty)
            {
                _logger.LogDebug("Cache miss for key: {Key}", key);
                return null;
            }

            _logger.LogDebug("Cache hit for key: {Key}", key);
            return JsonSerializer.Deserialize<T>(value!, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving cache key: {Key}", key);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null) where T : class
    {
        try
        {
            var json = JsonSerializer.Serialize(value, _jsonOptions);
            await _database.StringSetAsync(key, json, expiry);
            
            _logger.LogDebug("Cache set for key: {Key}, expiry: {Expiry}", key, expiry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting cache key: {Key}", key);
        }
    }

    public async Task<bool> DeleteAsync(string key)
    {
        try
        {
            var result = await _database.KeyDeleteAsync(key);
            _logger.LogDebug("Cache delete for key: {Key}, result: {Result}", key, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting cache key: {Key}", key);
            return false;
        }
    }

    public async Task<bool> ExistsAsync(string key)
    {
        try
        {
            return await _database.KeyExistsAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking cache key existence: {Key}", key);
            return false;
        }
    }

    public async Task<List<string>> GetKeysAsync(string pattern)
    {
        var keys = new List<string>();

        try
        {
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var redisKeys = server.Keys(pattern: pattern);

            foreach (var key in redisKeys)
            {
                keys.Add(key.ToString());
            }

            return keys;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting keys with pattern: {Pattern}", pattern);
            return keys;
        }
    }

    public async Task<long> IncrementAsync(string key, long value = 1, TimeSpan? expiry = null)
    {
        try
        {
            var result = await _database.StringIncrementAsync(key, value);
            
            if (expiry.HasValue)
            {
                await _database.KeyExpireAsync(key, expiry.Value);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error incrementing cache key: {Key}", key);
            throw;
        }
    }

    public async Task<bool> SetAddAsync(string key, string value, TimeSpan? expiry = null)
    {
        try
        {
            var result = await _database.SetAddAsync(key, value);
            
            if (expiry.HasValue && result)
            {
                await _database.KeyExpireAsync(key, expiry.Value);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding to set cache key: {Key}", key);
            return false;
        }
    }

    public async Task<string[]> SetMembersAsync(string key)
    {
        try
        {
            var values = await _database.SetMembersAsync(key);
            return values.Select(v => v.ToString()).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting set members for key: {Key}", key);
            return Array.Empty<string>();
        }
    }

    public async Task<bool> LockAsync(string key, string value, TimeSpan expiry)
    {
        try
        {
            return await _database.StringSetAsync(key, value, expiry, When.NotExists);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acquiring lock for key: {Key}", key);
            return false;
        }
    }

    public async Task<bool> UnlockAsync(string key, string value)
    {
        try
        {
            var script = @"
                if redis.call('get', KEYS[1]) == ARGV[1] then
                    return redis.call('del', KEYS[1])
                else
                    return 0
                end";

            var result = await _database.ScriptEvaluateAsync(script, new RedisKey[] { key }, new RedisValue[] { value });
            return (int)result == 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing lock for key: {Key}", key);
            return false;
        }
    }
}