using VoiceCode.ObserverService.Models;

namespace VoiceCode.ObserverService.Services;

public interface IStreamProcessor
{
    Task ProcessStreamEventAsync(SdkStreamEvent streamEvent, CancellationToken cancellationToken = default);
}