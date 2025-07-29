using VoiceCode.Common.Models;

namespace VoiceCode.Common.Interfaces;

public interface IAudioResponseTableService
{
    Task StoreAudioResponseAsync(string taskId, string sessionId, string audioUrl, string text, double duration);
    Task<AudioResponseEntity?> GetAudioResponseAsync(string taskId);
    Task<bool> AudioResponseExistsAsync(string taskId);
}