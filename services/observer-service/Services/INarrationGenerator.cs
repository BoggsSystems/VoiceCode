using VoiceCode.ObserverService.Models;

namespace VoiceCode.ObserverService.Services;

public interface INarrationGenerator
{
    Task<string?> GenerateNarrationAsync(SdkStreamEvent streamEvent, CancellationToken cancellationToken = default);
    bool ShouldNarrate(SdkStreamEvent streamEvent);
}