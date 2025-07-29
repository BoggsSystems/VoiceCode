using VoiceCode.Common.Interfaces;

namespace VoiceCode.RouterService.Services;

public interface IAudioResponseTracker
{
    Task StoreAudioResponseAsync(string taskId, AudioResponseData response);
    Task<AudioResponseData?> GetAudioResponseAsync(string taskId);
    Task<bool> HasAudioResponseAsync(string taskId);
}

public class AudioResponseTracker : IAudioResponseTracker
{
    private readonly ICacheService _cache;
    private readonly ILogger<AudioResponseTracker> _logger;
    private const string CacheKeyPrefix = "audio_response:";
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(10);

    public AudioResponseTracker(ICacheService cache, ILogger<AudioResponseTracker> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task StoreAudioResponseAsync(string taskId, AudioResponseData response)
    {
        var key = $"{CacheKeyPrefix}{taskId}";
        await _cache.SetAsync(key, response, CacheExpiry);
        _logger.LogInformation("Stored audio response for task {TaskId}", taskId);
    }

    public async Task<AudioResponseData?> GetAudioResponseAsync(string taskId)
    {
        var key = $"{CacheKeyPrefix}{taskId}";
        var response = await _cache.GetAsync<AudioResponseData>(key);
        
        if (response != null)
        {
            _logger.LogInformation("Retrieved audio response for task {TaskId}", taskId);
        }
        else
        {
            _logger.LogDebug("No audio response found for task {TaskId}", taskId);
        }
        
        return response;
    }

    public async Task<bool> HasAudioResponseAsync(string taskId)
    {
        var key = $"{CacheKeyPrefix}{taskId}";
        return await _cache.ExistsAsync(key);
    }
}

public class AudioResponseData
{
    public string TaskId { get; set; } = string.Empty;
    public string AudioUrl { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public double Duration { get; set; }
    public string Status { get; set; } = "ready";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}