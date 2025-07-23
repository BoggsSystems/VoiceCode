using VoiceCode.Common.DTOs;

namespace VoiceCode.Common.Models;

public class SynthesisRequest
{
    public string Text { get; set; } = string.Empty;
    public string VoiceName { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string OutputFormat { get; set; } = "audio-16khz-128kbitrate-mono-mp3";
    public double Speed { get; set; } = 1.0;
    public double Pitch { get; set; } = 1.0;
    public double Volume { get; set; } = 1.0;
    public string? Style { get; set; }
    public double? StyleDegree { get; set; }
    public string? RequestId { get; set; }
    public bool EnableWordBoundaryEvents { get; set; }
    public PersonalityProfile? Personality { get; set; }
}

public class SynthesisResult
{
    public string RequestId { get; set; } = string.Empty;
    public byte[] AudioData { get; set; } = Array.Empty<byte>();
    public string AudioUrl { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public int Duration { get; set; } // in milliseconds
    public List<WordBoundary> WordBoundaries { get; set; } = new();
    public DateTime ProcessedAt { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class StreamingSynthesisResult
{
    public string RequestId { get; set; } = string.Empty;
    public IAsyncEnumerable<AudioChunk>? AudioStream { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
}

public class AudioChunk
{
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public int Offset { get; set; }
    public int Duration { get; set; }
    public List<WordBoundary> WordBoundaries { get; set; } = new();
}

public class WordBoundary
{
    public string Word { get; set; } = string.Empty;
    public int AudioOffset { get; set; } // in milliseconds
    public int Duration { get; set; } // in milliseconds
    public int TextOffset { get; set; } // character position in original text
    public int TextLength { get; set; } // length of word in characters
}

public class VoiceProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string NeuralVoiceName { get; set; } = string.Empty;
    public Dictionary<string, string> Styles { get; set; } = new();
    public string SampleAudioUrl { get; set; } = string.Empty;
    public PersonalityProfile Personality { get; set; }
    public VoiceCharacteristics Characteristics { get; set; } = new();
}

public class VoiceCharacteristics
{
    public double DefaultSpeed { get; set; } = 1.0;
    public double DefaultPitch { get; set; } = 1.0;
    public string Tone { get; set; } = string.Empty; // "warm", "professional", "casual", etc.
    public string AgeGroup { get; set; } = string.Empty; // "young", "middle", "mature"
    public List<string> SupportedStyles { get; set; } = new();
}