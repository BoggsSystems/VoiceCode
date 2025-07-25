using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VoiceCode.Common.Models;

namespace VoiceCode.STTService.Services;

public interface ITranscriptionStreamManager
{
    Task RegisterStreamAsync(string sessionId, object streamSession);
    Task UnregisterStreamAsync(string sessionId);
    Task<StreamInfo> GetStreamInfoAsync(string sessionId);
    Task<List<StreamInfo>> GetActiveStreamsAsync();
    Task<bool> IsStreamActiveAsync(string sessionId);
    Task CleanupInactiveStreamsAsync();
}

public class TranscriptionStreamManager : ITranscriptionStreamManager, IHostedService, IDisposable
{
    private readonly ILogger<TranscriptionStreamManager> _logger;
    private readonly ConcurrentDictionary<string, StreamInfo> _activeStreams;
    private readonly ConcurrentDictionary<string, StreamHealthInfo> _streamHealth;
    private Timer _cleanupTimer;
    private readonly TimeSpan _inactivityTimeout = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(1);

    public TranscriptionStreamManager(ILogger<TranscriptionStreamManager> logger)
    {
        _logger = logger;
        _activeStreams = new ConcurrentDictionary<string, StreamInfo>();
        _streamHealth = new ConcurrentDictionary<string, StreamHealthInfo>();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Transcription Stream Manager");
        
        _cleanupTimer = new Timer(
            async _ => await CleanupInactiveStreamsAsync(),
            null,
            _cleanupInterval,
            _cleanupInterval);
        
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Transcription Stream Manager");
        
        _cleanupTimer?.Change(Timeout.Infinite, 0);
        
        // Clean up all active streams
        foreach (var stream in _activeStreams.Values)
        {
            try
            {
                UnregisterStreamAsync(stream.SessionId).Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up stream {SessionId}", stream.SessionId);
            }
        }
        
        return Task.CompletedTask;
    }

    public async Task RegisterStreamAsync(string sessionId, object streamSession)
    {
        var streamInfo = new StreamInfo
        {
            SessionId = sessionId,
            StartTime = DateTime.UtcNow,
            LastActivityTime = DateTime.UtcNow,
            IsActive = true,
            StreamReference = new WeakReference(streamSession)
        };

        _activeStreams.TryAdd(sessionId, streamInfo);
        _streamHealth.TryAdd(sessionId, new StreamHealthInfo { SessionId = sessionId });

        _logger.LogInformation("Registered stream {SessionId}, total active streams: {Count}",
            sessionId, _activeStreams.Count);
    }

    public async Task UnregisterStreamAsync(string sessionId)
    {
        if (_activeStreams.TryRemove(sessionId, out var streamInfo))
        {
            streamInfo.IsActive = false;
            streamInfo.EndTime = DateTime.UtcNow;
            
            _streamHealth.TryRemove(sessionId, out _);

            _logger.LogInformation("Unregistered stream {SessionId}, remaining active streams: {Count}",
                sessionId, _activeStreams.Count);
        }
    }

    public async Task<StreamInfo> GetStreamInfoAsync(string sessionId)
    {
        return _activeStreams.TryGetValue(sessionId, out var info) ? info : null;
    }

    public async Task<List<StreamInfo>> GetActiveStreamsAsync()
    {
        return _activeStreams.Values
            .Where(s => s.IsActive)
            .OrderBy(s => s.StartTime)
            .ToList();
    }

    public async Task<bool> IsStreamActiveAsync(string sessionId)
    {
        if (_activeStreams.TryGetValue(sessionId, out var info))
        {
            // Check if stream is still alive
            if (info.StreamReference.IsAlive)
            {
                UpdateStreamActivity(sessionId);
                return info.IsActive;
            }
            else
            {
                // Stream object has been garbage collected
                await UnregisterStreamAsync(sessionId);
                return false;
            }
        }
        
        return false;
    }

    public async Task CleanupInactiveStreamsAsync()
    {
        var now = DateTime.UtcNow;
        var inactiveStreams = _activeStreams
            .Where(kvp => kvp.Value.IsActive && 
                         (now - kvp.Value.LastActivityTime) > _inactivityTimeout)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var sessionId in inactiveStreams)
        {
            _logger.LogWarning("Cleaning up inactive stream {SessionId}", sessionId);
            await UnregisterStreamAsync(sessionId);
        }

        // Also cleanup dead weak references
        var deadStreams = _activeStreams
            .Where(kvp => !kvp.Value.StreamReference.IsAlive)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var sessionId in deadStreams)
        {
            _logger.LogWarning("Cleaning up dead stream reference {SessionId}", sessionId);
            await UnregisterStreamAsync(sessionId);
        }

        if (inactiveStreams.Count > 0 || deadStreams.Count > 0)
        {
            _logger.LogInformation("Cleaned up {InactiveCount} inactive and {DeadCount} dead streams",
                inactiveStreams.Count, deadStreams.Count);
        }
    }

    private void UpdateStreamActivity(string sessionId)
    {
        if (_activeStreams.TryGetValue(sessionId, out var info))
        {
            info.LastActivityTime = DateTime.UtcNow;
        }
        
        if (_streamHealth.TryGetValue(sessionId, out var health))
        {
            health.LastHeartbeat = DateTime.UtcNow;
            health.HeartbeatCount++;
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}

public class StreamInfo
{
    public string SessionId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime LastActivityTime { get; set; }
    public DateTime? EndTime { get; set; }
    public bool IsActive { get; set; }
    public WeakReference StreamReference { get; set; }
    
    public TimeSpan Duration => (EndTime ?? DateTime.UtcNow) - StartTime;
    public TimeSpan InactiveDuration => DateTime.UtcNow - LastActivityTime;
}

public class StreamHealthInfo
{
    public string SessionId { get; set; }
    public DateTime LastHeartbeat { get; set; }
    public int HeartbeatCount { get; set; }
    public int ErrorCount { get; set; }
    public DateTime? LastError { get; set; }
    public string LastErrorMessage { get; set; }
}