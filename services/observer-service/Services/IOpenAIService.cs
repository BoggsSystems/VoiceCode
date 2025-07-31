namespace VoiceCode.ObserverService.Services;

public interface IOpenAIService
{
    Task<string?> GenerateCompletionAsync(string prompt, CancellationToken cancellationToken = default);
}