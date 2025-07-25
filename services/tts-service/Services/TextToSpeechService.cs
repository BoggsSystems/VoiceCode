using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using VoiceCode.Common.Interfaces;
using VoiceCode.Common.Models;
using VoiceCode.Common.DTOs;
using Models = VoiceCode.Common.Models;
using DTOs = VoiceCode.Common.DTOs;
using VoiceCode.TTSService.Configuration;
using VoiceCode.TTSService.Services.Interfaces;
using System.Security.Cryptography;
using System.Text;

namespace VoiceCode.TTSService.Services;

public class TextToSpeechService : Interfaces.ITTSService, VoiceCode.Common.Interfaces.ITTSService, IDisposable
{
    private readonly ILogger<TextToSpeechService> _logger;
    private readonly IOptions<AzureSpeechOptions> _speechOptions;
    private readonly IOptions<VoiceOptions> _voiceOptions;
    private readonly Interfaces.IAudioStorageService _audioStorage;
    private readonly ICacheService _cache;
    private readonly IVoicePersonalityService _personalityService;
    private readonly ISSMLBuilder _ssmlBuilder;
    private readonly SpeechConfig? _speechConfig;
    private readonly SemaphoreSlim _semaphore;
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly AzureTTSRestService? _restService;
    private readonly IHttpClientFactory _httpClientFactory;

    public TextToSpeechService(
        ILogger<TextToSpeechService> logger,
        IOptions<AzureSpeechOptions> speechOptions,
        IOptions<VoiceOptions> voiceOptions,
        Interfaces.IAudioStorageService audioStorage,
        ICacheService cache,
        IVoicePersonalityService personalityService,
        ISSMLBuilder ssmlBuilder,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _speechOptions = speechOptions;
        _voiceOptions = voiceOptions;
        _audioStorage = audioStorage;
        _cache = cache;
        _personalityService = personalityService;
        _ssmlBuilder = ssmlBuilder;
        _httpClientFactory = httpClientFactory;

        // Try to configure Speech SDK, but don't fail if it doesn't work
        try
        {
            // Configure Speech SDK
            if (_speechOptions.Value.UseCustomEndpoint && !string.IsNullOrEmpty(_speechOptions.Value.Endpoint))
            {
                _speechConfig = SpeechConfig.FromEndpoint(
                    new Uri(_speechOptions.Value.Endpoint),
                    _speechOptions.Value.Key);
            }
            else
            {
                _speechConfig = SpeechConfig.FromSubscription(
                    _speechOptions.Value.Key,
                    _speechOptions.Value.Region);
            }

            _speechConfig.SpeechSynthesisVoiceName = _voiceOptions.Value.DefaultVoice;
            _speechConfig.SetSpeechSynthesisOutputFormat(GetSpeechSynthesisOutputFormat());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize Speech SDK, will use REST API fallback");
            _speechConfig = null;
        }

        // Initialize REST service as fallback
        var httpClient = _httpClientFactory.CreateClient("AzureTTS");
        var restLogger = loggerFactory.CreateLogger<AzureTTSRestService>();
        _restService = new AzureTTSRestService(httpClient, restLogger, _speechOptions);

        _semaphore = new SemaphoreSlim(_speechOptions.Value.MaxConcurrentSynthesis);

        _retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                3,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning(
                        "TTS retry attempt {RetryCount} after {TimeSpan}s: {Exception}",
                        retryCount, timeSpan.TotalSeconds, exception.Message);
                });
    }

    public async Task<SynthesisResult> SynthesizeAsync(SynthesisRequest request)
    {
        try
        {
            // Check cache if enabled
            if (_voiceOptions.Value.CacheAudio)
            {
                var cacheKey = GenerateCacheKey(request);
                var cachedResult = await _cache.GetAsync<SynthesisResult>(cacheKey);
                if (cachedResult != null)
                {
                    _logger.LogInformation("Returning cached audio for text hash {CacheKey}", cacheKey);
                    return cachedResult;
                }
            }

            // Get voice profile based on personality or voice name
            Models.VoiceProfile profile;
            if (request.Personality.HasValue)
            {
                profile = await _personalityService.GetPersonalityVoiceAsync(request.Personality.Value);
            }
            else if (!string.IsNullOrEmpty(request.VoiceName))
            {
                // Try to find profile by voice name
                var profiles = await _personalityService.GetAllProfilesAsync();
                profile = profiles.Values.FirstOrDefault(p => p.NeuralVoiceName == request.VoiceName)
                    ?? await _personalityService.GetVoiceProfileAsync("default");
            }
            else
            {
                profile = await _personalityService.GetVoiceProfileAsync("default");
            }
            
            var ssml = await BuildSSMLAsync(request.Text, profile, request.Style);

            // Synthesize speech
            var audioData = await SynthesizeSpeechAsync(ssml, profile);

            // Store audio to storage service
            var audioUrl = await _audioStorage.StoreAudioAsync(
                audioData,
                request.RequestId ?? Guid.NewGuid().ToString(),
                "mp3");

            var result = new SynthesisResult
            {
                RequestId = request.RequestId ?? Guid.NewGuid().ToString(),
                AudioData = audioData,
                AudioUrl = audioUrl,
                ContentType = "audio/mpeg",
                Duration = (int)CalculateDuration(audioData).TotalMilliseconds,
                ProcessedAt = DateTime.UtcNow,
                Metadata = new Dictionary<string, object>
                {
                    { "voiceName", profile.NeuralVoiceName },
                    { "language", profile.Language },
                    { "personality", profile.Personality.ToString() }
                }
            };

            // Cache result if enabled
            if (_voiceOptions.Value.CacheAudio)
            {
                var cacheKey = GenerateCacheKey(request);
                await _cache.SetAsync(cacheKey, result, _voiceOptions.Value.CacheDuration);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error synthesizing speech");
            throw new TTSException("Failed to synthesize speech", ex);
        }
    }

    public async Task<SynthesisResult> SynthesizeWithPersonalityAsync(
        string text,
        PersonalityProfile personality,
        string? emotion = null)
    {
        var profile = await _personalityService.GetPersonalityVoiceAsync(personality);
        var request = new SynthesisRequest
        {
            Text = text,
            VoiceName = profile.NeuralVoiceName,
            Language = profile.Language,
            Style = emotion ?? (profile.Styles.ContainsKey("default") ? profile.Styles["default"] : null),
            Personality = personality
        };

        return await SynthesizeAsync(request);
    }

    public async Task<StreamingSynthesisResult> StreamSynthesizeAsync(
        string text,
        string? voiceProfile = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var profile = await _personalityService.GetVoiceProfileAsync(voiceProfile ?? "default");
            var ssml = await BuildSSMLAsync(text, profile, null);

            var audioConfig = AudioConfig.FromStreamOutput(AudioOutputStream.CreatePullStream());
            using var synthesizer = new SpeechSynthesizer(_speechConfig, audioConfig);

            var tcs = new TaskCompletionSource<StreamingSynthesisResult>();
            var chunks = new List<AudioChunk>();
            var startTime = DateTime.UtcNow;
            var offset = 0;

            synthesizer.Synthesizing += (s, e) =>
            {
                if (e.Result.AudioData.Length > 0)
                {
                    chunks.Add(new AudioChunk
                    {
                        Data = e.Result.AudioData,
                        Offset = offset,
                        Duration = 0 // Will be calculated based on format
                    });
                    offset += e.Result.AudioData.Length;
                }
            };

            synthesizer.SynthesisCompleted += async (s, e) =>
            {
                var result = new StreamingSynthesisResult
                {
                    RequestId = Guid.NewGuid().ToString(),
                    AudioStream = CreateAsyncEnumerable(chunks),
                    ContentType = "audio/mpeg",
                    StartedAt = startTime
                };
                tcs.SetResult(result);
            };

            synthesizer.SynthesisCanceled += (s, e) =>
            {
                var cancellation = SpeechSynthesisCancellationDetails.FromResult(e.Result);
                if (cancellation.Reason == CancellationReason.Error)
                {
                    tcs.SetException(new TTSException($"Synthesis failed: {cancellation.ErrorCode} - {cancellation.ErrorDetails}"));
                }
                else
                {
                    tcs.SetCanceled();
                }
            };

            await synthesizer.SpeakSsmlAsync(ssml);
            return await tcs.Task;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in streaming synthesis");
            throw new TTSException("Failed to stream synthesize speech", ex);
        }
    }

    private async Task<string> BuildSSMLAsync(string text, Models.VoiceProfile profile, string? emotion)
    {
        if (!_voiceOptions.Value.EnableSSML)
        {
            return text;
        }

        return await _ssmlBuilder.BuildAsync(text, profile, emotion);
    }

    private async Task<byte[]> SynthesizeSpeechAsync(string ssml, Models.VoiceProfile profile)
    {
        await _semaphore.WaitAsync();
        try
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                // Try SDK first if available
                if (_speechConfig != null)
                {
                    try
                    {
                        using var synthesizer = new SpeechSynthesizer(_speechConfig);
                        
                        var result = await synthesizer.SpeakSsmlAsync(ssml)
                            .ConfigureAwait(false);

                        if (result.Reason == ResultReason.SynthesizingAudioCompleted)
                        {
                            return result.AudioData;
                        }
                        else if (result.Reason == ResultReason.Canceled)
                        {
                            var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
                            throw new TTSException(
                                $"Speech synthesis canceled: {cancellation.Reason} - {cancellation.ErrorDetails}");
                        }

                        throw new TTSException("Speech synthesis failed");
                    }
                    catch (Exception ex) when (ex.Message.Contains("Failed to initialize platform"))
                    {
                        _logger.LogWarning("Speech SDK failed, falling back to REST API");
                        // Fall through to REST API
                    }
                }

                // Use REST API as fallback
                if (_restService != null)
                {
                    _logger.LogInformation("Using REST API for speech synthesis");
                    return await _restService.SynthesizeSpeechAsync(ssml, profile);
                }

                throw new TTSException("No TTS service available");
            });
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private SpeechSynthesisOutputFormat GetSpeechSynthesisOutputFormat()
    {
        return _voiceOptions.Value.DefaultFormat switch
        {
            AudioOutputFormat.Audio16Khz32KBitRateMonoMp3 => SpeechSynthesisOutputFormat.Audio16Khz32KBitRateMonoMp3,
            AudioOutputFormat.Audio16Khz64KBitRateMonoMp3 => SpeechSynthesisOutputFormat.Audio16Khz64KBitRateMonoMp3,
            AudioOutputFormat.Audio16Khz128KBitRateMonoMp3 => SpeechSynthesisOutputFormat.Audio16Khz128KBitRateMonoMp3,
            AudioOutputFormat.Audio24Khz48KBitRateMonoMp3 => SpeechSynthesisOutputFormat.Audio24Khz48KBitRateMonoMp3,
            AudioOutputFormat.Audio24Khz96KBitRateMonoMp3 => SpeechSynthesisOutputFormat.Audio24Khz96KBitRateMonoMp3,
            AudioOutputFormat.Audio24Khz160KBitRateMonoMp3 => SpeechSynthesisOutputFormat.Audio24Khz160KBitRateMonoMp3,
            AudioOutputFormat.Audio48Khz96KBitRateMonoMp3 => SpeechSynthesisOutputFormat.Audio48Khz96KBitRateMonoMp3,
            AudioOutputFormat.Audio48Khz192KBitRateMonoMp3 => SpeechSynthesisOutputFormat.Audio48Khz192KBitRateMonoMp3,
            AudioOutputFormat.Ogg16Khz16BitMonoOpus => SpeechSynthesisOutputFormat.Ogg16Khz16BitMonoOpus,
            AudioOutputFormat.Ogg24Khz16BitMonoOpus => SpeechSynthesisOutputFormat.Ogg24Khz16BitMonoOpus,
            AudioOutputFormat.Raw16Khz16BitMonoPcm => SpeechSynthesisOutputFormat.Raw16Khz16BitMonoPcm,
            AudioOutputFormat.Raw24Khz16BitMonoPcm => SpeechSynthesisOutputFormat.Raw24Khz16BitMonoPcm,
            AudioOutputFormat.Raw48Khz16BitMonoPcm => SpeechSynthesisOutputFormat.Raw48Khz16BitMonoPcm,
            _ => SpeechSynthesisOutputFormat.Audio16Khz32KBitRateMonoMp3
        };
    }

    private async IAsyncEnumerable<AudioChunk> CreateAsyncEnumerable(List<AudioChunk> chunks)
    {
        foreach (var chunk in chunks)
        {
            yield return chunk;
        }
    }

    private string GetAudioFormat(string voice)
    {
        return _voiceOptions.Value.DefaultFormat.ToString().ToLower();
    }

    private string GetFileExtension(string voice)
    {
        return _voiceOptions.Value.DefaultFormat.ToString().Contains("Mp3") ? ".mp3" :
               _voiceOptions.Value.DefaultFormat.ToString().Contains("Opus") ? ".opus" :
               ".pcm";
    }

    private TimeSpan CalculateDuration(byte[] audioData)
    {
        // Simplified duration calculation based on format and data size
        var bitRate = GetBitRate();
        var seconds = (audioData.Length * 8.0) / bitRate;
        return TimeSpan.FromSeconds(seconds);
    }

    private int GetBitRate()
    {
        return _voiceOptions.Value.DefaultFormat switch
        {
            AudioOutputFormat.Audio16Khz32KBitRateMonoMp3 => 32000,
            AudioOutputFormat.Audio16Khz64KBitRateMonoMp3 => 64000,
            AudioOutputFormat.Audio16Khz128KBitRateMonoMp3 => 128000,
            AudioOutputFormat.Audio24Khz48KBitRateMonoMp3 => 48000,
            AudioOutputFormat.Audio24Khz96KBitRateMonoMp3 => 96000,
            AudioOutputFormat.Audio24Khz160KBitRateMonoMp3 => 160000,
            AudioOutputFormat.Audio48Khz96KBitRateMonoMp3 => 96000,
            AudioOutputFormat.Audio48Khz192KBitRateMonoMp3 => 192000,
            _ => 32000
        };
    }

    private string GenerateCacheKey(SynthesisRequest request)
    {
        var key = $"{request.Text}:{request.VoiceName}:{request.Style}";
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
        return Convert.ToBase64String(hash);
    }

    // Implement Common.Interfaces.ITTSService
    public async Task<AudioResult> GenerateSpeechAsync(TTSRequest request)
    {
        try
        {
            var synthesisRequest = new SynthesisRequest
            {
                Text = request.Text,
                VoiceName = request.Voice,
                RequestId = request.SessionId,
                Speed = request.SpeechStyle != null ? double.Parse(request.SpeechStyle.Rate) : 1.0,
                Pitch = request.SpeechStyle != null ? (request.SpeechStyle.Pitch.Contains("%") 
                    ? 1.0 + (double.Parse(request.SpeechStyle.Pitch.Replace("%", "").Replace("+", "")) / 100.0)
                    : 1.0) : 1.0
            };

            var result = await SynthesizeAsync(synthesisRequest);

            return new AudioResult
            {
                Success = true,
                AudioUrl = result.AudioUrl,
                AudioData = result.AudioData,
                DurationMs = result.Duration,
                FromCache = false // Could check if it was from cache
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating speech");
            return new AudioResult
            {
                Success = false,
                AudioUrl = string.Empty
            };
        }
    }

    public async Task<List<Voice>> GetAvailableVoicesAsync(string language)
    {
        var profiles = await _personalityService.GetAllProfilesAsync();
        return profiles.Values
            .Where(p => p.Language.StartsWith(language))
            .Select(p => new Voice
            {
                Name = p.NeuralVoiceName,
                DisplayName = p.DisplayName,
                Language = p.Language,
                Gender = p.Gender
            })
            .ToList();
    }

    public void Dispose()
    {
        _semaphore?.Dispose();
    }
}

public class TTSException : Exception
{
    public TTSException(string message) : base(message) { }
    public TTSException(string message, Exception innerException) : base(message, innerException) { }
}