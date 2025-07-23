namespace VoiceCode.TTSService.Configuration;

public class AzureSpeechOptions
{
    public string Key { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public bool UseCustomEndpoint { get; set; }
    public int MaxConcurrentSynthesis { get; set; } = 50;
    public TimeSpan SynthesisTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

public class VoiceOptions
{
    public Dictionary<string, ConfigVoiceProfile> Profiles { get; set; } = new();
    public string DefaultVoice { get; set; } = "en-US-JennyNeural";
    public string DefaultLanguage { get; set; } = "en-US";
    public AudioOutputFormat DefaultFormat { get; set; } = AudioOutputFormat.Audio16Khz32KBitRateMonoMp3;
    public bool EnableSSML { get; set; } = true;
    public bool CacheAudio { get; set; } = true;
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromHours(24);
}

public class ConfigVoiceProfile
{
    public string Name { get; set; } = string.Empty;
    public string Voice { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public SpeechStyle Style { get; set; } = new();
    public ProsodySettings Prosody { get; set; } = new();
}

public class SpeechStyle
{
    public string Style { get; set; } = "friendly";
    public double StyleDegree { get; set; } = 1.0;
    public string Role { get; set; } = "Assistant";
}

public class ProsodySettings
{
    public string Rate { get; set; } = "1.0";
    public string Pitch { get; set; } = "0%";
    public string Volume { get; set; } = "100";
}

public enum AudioOutputFormat
{
    Audio16Khz32KBitRateMonoMp3,
    Audio16Khz64KBitRateMonoMp3,
    Audio16Khz128KBitRateMonoMp3,
    Audio24Khz48KBitRateMonoMp3,
    Audio24Khz96KBitRateMonoMp3,
    Audio24Khz160KBitRateMonoMp3,
    Audio48Khz96KBitRateMonoMp3,
    Audio48Khz192KBitRateMonoMp3,
    Ogg16Khz16BitMonoOpus,
    Ogg24Khz16BitMonoOpus,
    Raw16Khz16BitMonoPcm,
    Raw24Khz16BitMonoPcm,
    Raw48Khz16BitMonoPcm
}