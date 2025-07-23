using VoiceCode.Common.Models;
using VoiceCode.Common.DTOs;
using Models = VoiceCode.Common.Models;

namespace VoiceCode.TTSService.Services.Interfaces;

public interface ITTSService
{
    Task<SynthesisResult> SynthesizeAsync(SynthesisRequest request);
    Task<SynthesisResult> SynthesizeWithPersonalityAsync(string text, PersonalityProfile personality, string? emotion = null);
    Task<StreamingSynthesisResult> StreamSynthesizeAsync(string text, string? voiceProfile = null, CancellationToken cancellationToken = default);
}

public interface IAudioStorageService
{
    Task<string> StoreAudioAsync(byte[] audioData, string sessionId, string fileExtension);
    Task<byte[]?> GetAudioAsync(string audioUrl);
    Task<bool> DeleteAudioAsync(string audioUrl);
    Task<List<string>> ListAudioFilesAsync(string sessionId);
}

public interface IVoicePersonalityService
{
    Task<VoiceProfile> GetVoiceProfileAsync(string profileName);
    Task<VoiceProfile> GetPersonalityVoiceAsync(PersonalityProfile personality);
    Task<Dictionary<string, VoiceProfile>> GetAllProfilesAsync();
}

public interface ISSMLBuilder
{
    Task<string> BuildAsync(string text, VoiceCode.Common.Models.VoiceProfile profile, string? emotion = null);
    string AddEmphasis(string text, string level = "moderate");
    string AddPause(string duration = "500ms");
    string AddProsody(string text, string rate = "1.0", string pitch = "0%", string volume = "100");
}