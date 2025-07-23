using Microsoft.Extensions.Options;
using VoiceCode.Common.Models;
using VoiceCode.Common.DTOs;
using Models = VoiceCode.Common.Models;
using VoiceCode.TTSService.Configuration;
using VoiceCode.TTSService.Services.Interfaces;

namespace VoiceCode.TTSService.Services;

public class VoicePersonalityService : IVoicePersonalityService
{
    private readonly ILogger<VoicePersonalityService> _logger;
    private readonly IOptions<VoiceOptions> _voiceOptions;
    private readonly Dictionary<PersonalityProfile, Models.VoiceProfile> _personalityMappings;

    public VoicePersonalityService(
        ILogger<VoicePersonalityService> logger,
        IOptions<VoiceOptions> voiceOptions)
    {
        _logger = logger;
        _voiceOptions = voiceOptions;
        _personalityMappings = InitializePersonalityMappings();
    }

    public Task<Models.VoiceProfile> GetVoiceProfileAsync(string profileName)
    {
        if (_voiceOptions.Value.Profiles.TryGetValue(profileName, out var configProfile))
        {
            return Task.FromResult(ConvertToModelProfile(profileName, configProfile));
        }

        // Return default profile
        var defaultProfile = new Models.VoiceProfile
        {
            Id = "default",
            Name = "default",
            DisplayName = "Default Voice",
            Language = _voiceOptions.Value.DefaultLanguage,
            Gender = "Female",
            NeuralVoiceName = _voiceOptions.Value.DefaultVoice,
            Styles = new Dictionary<string, string> { { "default", "friendly" } },
            Personality = PersonalityProfile.FriendlyAssistant,
            Characteristics = new VoiceCharacteristics
            {
                DefaultSpeed = 1.0,
                DefaultPitch = 1.0,
                Tone = "warm",
                AgeGroup = "young"
            }
        };

        return Task.FromResult(defaultProfile);
    }

    public Task<Models.VoiceProfile> GetPersonalityVoiceAsync(PersonalityProfile personality)
    {
        if (_personalityMappings.TryGetValue(personality, out var profile))
        {
            return Task.FromResult(profile);
        }

        return GetVoiceProfileAsync("default");
    }

    public Task<Dictionary<string, Models.VoiceProfile>> GetAllProfilesAsync()
    {
        var modelProfiles = new Dictionary<string, Models.VoiceProfile>();
        foreach (var kvp in _voiceOptions.Value.Profiles)
        {
            modelProfiles[kvp.Key] = ConvertToModelProfile(kvp.Key, kvp.Value);
        }
        return Task.FromResult(modelProfiles);
    }
    
    private Models.VoiceProfile ConvertToModelProfile(string id, ConfigVoiceProfile configProfile)
    {
        return new Models.VoiceProfile
        {
            Id = id,
            Name = configProfile.Name,
            DisplayName = configProfile.Name,
            Language = configProfile.Language,
            Gender = "Female", // Default, could be extended
            NeuralVoiceName = configProfile.Voice,
            Styles = new Dictionary<string, string> { { "default", configProfile.Style.Style } },
            Personality = PersonalityProfile.FriendlyAssistant, // Default
            Characteristics = new VoiceCharacteristics
            {
                DefaultSpeed = double.Parse(configProfile.Prosody.Rate),
                DefaultPitch = configProfile.Prosody.Pitch.Contains("%") 
                    ? 1.0 + (double.Parse(configProfile.Prosody.Pitch.Replace("%", "")) / 100.0)
                    : 1.0,
                Tone = "warm",
                AgeGroup = "young"
            }
        };
    }

    private Dictionary<PersonalityProfile, Models.VoiceProfile> InitializePersonalityMappings()
    {
        return new Dictionary<PersonalityProfile, Models.VoiceProfile>
        {
            [PersonalityProfile.FriendlyAssistant] = new Models.VoiceProfile
            {
                Id = "friendly",
                Name = "friendly",
                DisplayName = "Friendly Assistant",
                Language = "en-US",
                Gender = "Female",
                NeuralVoiceName = "en-US-JennyNeural",
                Styles = new Dictionary<string, string> { { "default", "friendly" } },
                Personality = PersonalityProfile.FriendlyAssistant,
                Characteristics = new VoiceCharacteristics
                {
                    DefaultSpeed = 1.05,
                    DefaultPitch = 1.05,
                    Tone = "warm",
                    AgeGroup = "young"
                }
            },
            [PersonalityProfile.ProfessionalCoPilot] = new Models.VoiceProfile
            {
                Id = "professional",
                Name = "professional",
                DisplayName = "Professional Co-Pilot",
                Language = "en-US", 
                Gender = "Female",
                NeuralVoiceName = "en-US-AriaNeural",
                Styles = new Dictionary<string, string> { { "default", "professional" } },
                Personality = PersonalityProfile.ProfessionalCoPilot,
                Characteristics = new VoiceCharacteristics
                {
                    DefaultSpeed = 0.95,
                    DefaultPitch = 1.0,
                    Tone = "professional",
                    AgeGroup = "middle"
                }
            },
            [PersonalityProfile.CasualBuddy] = new Models.VoiceProfile
            {
                Id = "casual",
                Name = "casual",
                DisplayName = "Casual Buddy",
                Language = "en-US",
                Gender = "Male",
                NeuralVoiceName = "en-US-GuyNeural",
                Styles = new Dictionary<string, string> { { "default", "casual" } },
                Personality = PersonalityProfile.CasualBuddy,
                Characteristics = new VoiceCharacteristics
                {
                    DefaultSpeed = 1.1,
                    DefaultPitch = 1.02,
                    Tone = "casual",
                    AgeGroup = "young"
                }
            },
            [PersonalityProfile.Minimalist] = new Models.VoiceProfile
            {
                Id = "minimalist",
                Name = "minimalist", 
                DisplayName = "Minimalist",
                Language = "en-US",
                Gender = "Male",
                NeuralVoiceName = "en-US-JasonNeural",
                Styles = new Dictionary<string, string> { { "default", "calm" } },
                Personality = PersonalityProfile.Minimalist,
                Characteristics = new VoiceCharacteristics
                {
                    DefaultSpeed = 1.0,
                    DefaultPitch = 0.98,
                    Tone = "calm",
                    AgeGroup = "mature"
                }
            }
        };
    }
}