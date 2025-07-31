using Microsoft.Extensions.Options;
using VoiceCode.ObserverService.Configuration;
using VoiceCode.ObserverService.Models;

namespace VoiceCode.ObserverService.Services;

public class StreamProcessor : IStreamProcessor
{
    private readonly ILogger<StreamProcessor> _logger;
    private readonly INarrationGenerator _narrationGenerator;
    private readonly IServiceBusPublisher _serviceBusPublisher;
    private readonly ObserverOptions _options;
    private readonly Dictionary<string, DateTime> _lastNarrationTime = new();
    private readonly SemaphoreSlim _throttleSemaphore;

    public StreamProcessor(
        ILogger<StreamProcessor> logger,
        INarrationGenerator narrationGenerator,
        IServiceBusPublisher serviceBusPublisher,
        IOptions<ObserverOptions> options)
    {
        _logger = logger;
        _narrationGenerator = narrationGenerator;
        _serviceBusPublisher = serviceBusPublisher;
        _options = options.Value;
        _throttleSemaphore = new SemaphoreSlim(_options.MaxConcurrentStreams);
    }

    public async Task ProcessStreamEventAsync(SdkStreamEvent streamEvent, CancellationToken cancellationToken = default)
    {
        await _throttleSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_options.EnableDebugLogging)
            {
                _logger.LogDebug("Processing stream event: {EventType} for session {SessionId}", 
                    streamEvent.EventType, streamEvent.SessionId);
            }

            // Check if we should narrate this event
            if (!_narrationGenerator.ShouldNarrate(streamEvent))
            {
                return;
            }

            // Apply throttling per session
            var sessionKey = $"{streamEvent.SessionId}:{streamEvent.WorkerId}";
            if (_lastNarrationTime.TryGetValue(sessionKey, out var lastTime))
            {
                var timeSinceLastNarration = DateTime.UtcNow - lastTime;
                if (timeSinceLastNarration.TotalMilliseconds < _options.NarrationThrottleMs)
                {
                    _logger.LogDebug("Throttling narration for session {SessionId}", streamEvent.SessionId);
                    return;
                }
            }

            // Generate narration
            var narrationText = await _narrationGenerator.GenerateNarrationAsync(streamEvent, cancellationToken);
            if (string.IsNullOrWhiteSpace(narrationText))
            {
                return;
            }

            // Update last narration time
            _lastNarrationTime[sessionKey] = DateTime.UtcNow;

            // Determine priority based on event type
            var priority = DeterminePriority(streamEvent.EventType);

            // Publish narration request
            var narrationRequest = new NarrationRequest
            {
                SessionId = streamEvent.SessionId,
                WorkerId = streamEvent.WorkerId,
                Text = narrationText,
                Priority = priority
            };

            await _serviceBusPublisher.PublishNarrationAsync(narrationRequest, cancellationToken);

            _logger.LogInformation("Published narration for event {EventType} in session {SessionId}", 
                streamEvent.EventType, streamEvent.SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing stream event for session {SessionId}", streamEvent.SessionId);
        }
        finally
        {
            _throttleSemaphore.Release();
        }
    }

    private NarrationPriority DeterminePriority(string eventType)
    {
        return eventType switch
        {
            SdkEventTypes.Error => NarrationPriority.Critical,
            SdkEventTypes.FileCreate or SdkEventTypes.FileWrite => NarrationPriority.High,
            SdkEventTypes.CodeGeneration => NarrationPriority.High,
            SdkEventTypes.Progress => NarrationPriority.Low,
            _ => NarrationPriority.Normal
        };
    }
}