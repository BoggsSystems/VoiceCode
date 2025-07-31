using VoiceCode.ObserverService.Models;

namespace VoiceCode.ObserverService.Services;

public interface IServiceBusPublisher
{
    Task PublishNarrationAsync(NarrationRequest narration, CancellationToken cancellationToken = default);
}