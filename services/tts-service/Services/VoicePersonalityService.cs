using Microsoft.Extensions.Options;
using VoiceCode.Common.Models;
using VoiceCode.TTSService.Configuration;
using VoiceCode.TTSService.Services.Interfaces;

namespace VoiceCode.TTSService.Services;

public class VoicePersonalityService : IVoicePersonalityService
{
    private readonly ILogger<VoicePersonalityService> _logger;
    private readonly IOptions<VoiceOptions> _voiceOptions;
    private readonly Dictionary<PersonalityProfile, VoiceProfile> _personalityMappings;

    public VoicePersonalityService(
        ILogger<VoicePersonalityService> logger,
        IOptions<VoiceOptions> voiceOptions)
    {
        _logger = logger;
        _voiceOptions = voiceOptions;
        _personalityMappings = InitializePersonalityMappings();
    }

    public Task<VoiceProfile> GetVoiceProfileAsync(string profileName)
    {
        if (_voiceOptions.Value.Profiles.TryGetValue(profileName, out var profile))
        {
            return Task.FromResult(profile);
        }

        // Return default profile
        var defaultProfile = new VoiceProfile
        {
            Name = "default",
            Voice = _voiceOptions.Value.DefaultVoice,
            Language = _voiceOptions.Value.DefaultLanguage,
            Style = new SpeechStyle { Style = "friendly", StyleDegree = 1.0 },
            Prosody = new ProsodySettings { Rate = "1.0", Pitch = "0%", Volume = "100" }
        };

        return Task.FromResult(defaultProfile);
    }

    public Task<VoiceProfile> GetPersonalityVoiceAsync(PersonalityProfile personality)
    {
        if (_personalityMappings.TryGetValue(personality, out var profile))
        {
            return Task.FromResult(profile);
        }

        return GetVoiceProfileAsync("default");
    }

    public Task<Dictionary<string, VoiceProfile>> GetAllProfilesAsync()
    {
        return Task.FromResult(_voiceOptions.Value.Profiles);
    }

    private Dictionary<PersonalityProfile, VoiceProfile> InitializePersonalityMappings()
    {
        return new Dictionary<PersonalityProfile, VoiceProfile>
        {
            [PersonalityProfile.Friendly] = new VoiceProfile
            {
                Name = "friendly",
                Voice = "en-US-JennyNeural",
                Language = "en-US",
                Style = new SpeechStyle 
                { 
                    Style = "friendly", 
                    StyleDegree = 1.2,
                    Role = "Friend"
                },
                Prosody = new ProsodySettings 
                { 
                    Rate = "1.05", 
                    Pitch = "+5%", 
                    Volume = "105"
                }
            },
            [PersonalityProfile.Professional] = new VoiceProfile
            {
                Name = "professional",
                Voice = "en-US-AriaNeural",
                Language = "en-US",
                Style = new SpeechStyle 
                { 
                    Style = "professional", 
                    StyleDegree = 1.0,
                    Role = "Professional"
                },
                Prosody = new ProsodySettings 
                { 
                    Rate = "0.95", 
                    Pitch = "0%", 
                    Volume = "100"
                }
            },
            [PersonalityProfile.Casual] = new VoiceProfile
            {
                Name = "casual",
                Voice = "en-US-GuyNeural",
                Language = "en-US",
                Style = new SpeechStyle 
                { 
                    Style = "casual", 
                    StyleDegree = 1.1,
                    Role = "Colleague"
                },
                Prosody = new ProsodySettings 
                { 
                    Rate = "1.1", 
                    Pitch = "+2%", 
                    Volume = "102"
                }
            },
            [PersonalityProfile.Minimalist] = new VoiceProfile
            {
                Name = "minimalist",
                Voice = "en-US-JasonNeural",
                Language = "en-US",
                Style = new SpeechStyle 
                { 
                    Style = "calm", 
                    StyleDegree = 0.8,
                    Role = "Assistant"
                },
                Prosody = new ProsodySettings 
                { 
                    Rate = "1.0", 
                    Pitch = "-2%", 
                    Volume = "95"
                }
            }
        };
    }
}