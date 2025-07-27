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
        var startTime = DateTime.UtcNow;
        var requestId = request.RequestId ?? Guid.NewGuid().ToString();
        
        _logger.LogInformation("TTS Synthesis Starting - RequestId: {RequestId}, SessionId: {SessionId}, TextLength: {TextLength}, Voice: {Voice}",
            requestId, request.SessionId ?? "none", request.Text?.Length ?? 0, request.VoiceName ?? "default");
        
        try
        {
            // Check cache if enabled
            if (_voiceOptions.Value.CacheAudio)
            {
                var cacheKey = GenerateCacheKey(request);
                var cachedResult = await _cache.GetAsync<SynthesisResult>(cacheKey);
                if (cachedResult != null)
                {
                    _logger.LogInformation("TTS Cache Hit - RequestId: {RequestId}, CacheKey: {CacheKey}, AudioSize: {Size} bytes",
                        requestId, cacheKey, cachedResult.AudioData?.Length ?? 0);
                    return cachedResult;
                }
                else
                {
                    _logger.LogDebug("TTS Cache Miss - RequestId: {RequestId}, CacheKey: {CacheKey}",
                        requestId, cacheKey);
                }
            }

            // Get voice profile based on personality or voice name
            Models.VoiceProfile profile;
            if (request.Personality.HasValue)
            {
                _logger.LogDebug("Loading personality voice profile - RequestId: {RequestId}, Personality: {Personality}",
                    requestId, request.Personality.Value);
                profile = await _personalityService.GetPersonalityVoiceAsync(request.Personality.Value);
            }
            else if (!string.IsNullOrEmpty(request.VoiceName))
            {
                _logger.LogDebug("Loading voice profile by name - RequestId: {RequestId}, VoiceName: {VoiceName}",
                    requestId, request.VoiceName);
                // Try to find profile by voice name
                var profiles = await _personalityService.GetAllProfilesAsync();
                profile = profiles.Values.FirstOrDefault(p => p.NeuralVoiceName == request.VoiceName)
                    ?? await _personalityService.GetVoiceProfileAsync("default");
            }
            else
            {
                _logger.LogDebug("Using default voice profile - RequestId: {RequestId}", requestId);
                profile = await _personalityService.GetVoiceProfileAsync("default");
            }
            
            _logger.LogInformation("Voice Profile Selected - RequestId: {RequestId}, Voice: {Voice}, Language: {Language}, Personality: {Personality}",
                requestId, profile.NeuralVoiceName, profile.Language, profile.Personality);
            
            var ssml = await BuildSSMLAsync(request.Text, profile, request.Style);
            _logger.LogDebug("SSML Generated - RequestId: {RequestId}, SSMLLength: {Length}",
                requestId, ssml?.Length ?? 0);

            // Synthesize speech
            _logger.LogInformation("Starting speech synthesis - RequestId: {RequestId}", requestId);
            var synthStartTime = DateTime.UtcNow;
            var audioData = await SynthesizeSpeechAsync(ssml, profile);
            var synthDuration = (DateTime.UtcNow - synthStartTime).TotalMilliseconds;
            
            _logger.LogInformation("Speech synthesis completed - RequestId: {RequestId}, AudioSize: {Size} bytes, SynthTime: {Time}ms",
                requestId, audioData?.Length ?? 0, synthDuration);

            // Store audio to storage service
            _logger.LogDebug("Storing audio to storage - RequestId: {RequestId}", requestId);
            var storeStartTime = DateTime.UtcNow;
            var audioUrl = await _audioStorage.StoreAudioAsync(
                audioData,
                requestId,
                "mp3");
            var storeDuration = (DateTime.UtcNow - storeStartTime).TotalMilliseconds;
            
            _logger.LogInformation("Audio stored - RequestId: {RequestId}, URL: {URL}, StoreTime: {Time}ms",
                requestId, audioUrl, storeDuration);

            var audioDuration = CalculateDuration(audioData);
            var result = new SynthesisResult
            {
                RequestId = requestId,
                AudioData = audioData,
                AudioUrl = audioUrl,
                ContentType = "audio/mpeg",
                Duration = (int)audioDuration.TotalMilliseconds,
                ProcessedAt = DateTime.UtcNow,
                Metadata = new Dictionary<string, object>
                {
                    { "voiceName", profile.NeuralVoiceName },
                    { "language", profile.Language },
                    { "personality", profile.Personality.ToString() },
                    { "synthesisTimeMs", synthDuration },
                    { "storageTimeMs", storeDuration }
                }
            };
            
            _logger.LogInformation("TTS Result Created - RequestId: {RequestId}, AudioDuration: {Duration}ms",
                requestId, result.Duration);

            // Cache result if enabled
            if (_voiceOptions.Value.CacheAudio)
            {
                var cacheKey = GenerateCacheKey(request);
                _logger.LogDebug("Caching TTS result - RequestId: {RequestId}, CacheKey: {CacheKey}, Duration: {Duration}",
                    requestId, cacheKey, _voiceOptions.Value.CacheDuration);
                await _cache.SetAsync(cacheKey, result, _voiceOptions.Value.CacheDuration);
            }

            var totalDuration = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogInformation("TTS Synthesis Complete - RequestId: {RequestId}, SessionId: {SessionId}, TotalTime: {Time}ms, AudioSize: {Size} bytes, AudioUrl: {Url}",
                requestId, request.SessionId ?? "none", totalDuration, audioData?.Length ?? 0, audioUrl);
                
            return result;
        }
        catch (Exception ex)
        {
            var failureDuration = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogError(ex, "TTS Synthesis Failed - RequestId: {RequestId}, SessionId: {SessionId}, FailureTime: {Time}ms",
                requestId, request.SessionId ?? "none", failureDuration);
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
        _logger.LogDebug("Acquiring synthesis semaphore - Voice: {Voice}", profile.NeuralVoiceName);
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
                        _logger.LogDebug("Attempting synthesis with Speech SDK - Voice: {Voice}", profile.NeuralVoiceName);
                        using var synthesizer = new SpeechSynthesizer(_speechConfig);
                        
                        var result = await synthesizer.SpeakSsmlAsync(ssml)
                            .ConfigureAwait(false);

                        if (result.Reason == ResultReason.SynthesizingAudioCompleted)
                        {
                            _logger.LogDebug("Speech SDK synthesis successful - AudioSize: {Size} bytes", result.AudioData.Length);
                            return result.AudioData;
                        }
                        else if (result.Reason == ResultReason.Canceled)
                        {
                            var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
                            _logger.LogError("Speech SDK synthesis canceled - Reason: {Reason}, ErrorCode: {ErrorCode}, Details: {Details}",
                                cancellation.Reason, cancellation.ErrorCode, cancellation.ErrorDetails);
                            throw new TTSException(
                                $"Speech synthesis canceled: {cancellation.Reason} - {cancellation.ErrorDetails}");
                        }

                        _logger.LogError("Speech SDK synthesis failed - Reason: {Reason}", result.Reason);
                        throw new TTSException("Speech synthesis failed");
                    }
                    catch (Exception ex) when (ex.Message.Contains("Failed to initialize platform"))
                    {
                        _logger.LogWarning(ex, "Speech SDK platform initialization failed, falling back to REST API");
                        // Fall through to REST API
                    }
                }

                // Use REST API as fallback
                if (_restService != null)
                {
                    _logger.LogInformation("Using REST API for speech synthesis - Voice: {Voice}", profile.NeuralVoiceName);
                    var audioData = await _restService.SynthesizeSpeechAsync(ssml, profile);
                    _logger.LogDebug("REST API synthesis successful - AudioSize: {Size} bytes", audioData.Length);
                    return audioData;
                }

                _logger.LogError("No TTS service available - SDK: {SdkAvailable}, REST: {RestAvailable}",
                    _speechConfig != null, _restService != null);
                throw new TTSException("No TTS service available");
            });
        }
        finally
        {
            _semaphore.Release();
            _logger.LogDebug("Released synthesis semaphore - Voice: {Voice}", profile.NeuralVoiceName);
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
        var requestId = request.SessionId ?? Guid.NewGuid().ToString();
        _logger.LogInformation("GenerateSpeechAsync called - RequestId: {RequestId}, TextLength: {Length}, Voice: {Voice}",
            requestId, request.Text?.Length ?? 0, request.Voice);
        
        try
        {
            var synthesisRequest = new SynthesisRequest
            {
                Text = request.Text,
                VoiceName = request.Voice,
                RequestId = requestId,
                SessionId = request.SessionId,
                Speed = request.SpeechStyle != null ? double.Parse(request.SpeechStyle.Rate) : 1.0,
                Pitch = request.SpeechStyle != null ? (request.SpeechStyle.Pitch.Contains("%") 
                    ? 1.0 + (double.Parse(request.SpeechStyle.Pitch.Replace("%", "").Replace("+", "")) / 100.0)
                    : 1.0) : 1.0
            };
            
            _logger.LogDebug("Synthesis parameters - RequestId: {RequestId}, Speed: {Speed}, Pitch: {Pitch}",
                requestId, synthesisRequest.Speed, synthesisRequest.Pitch);

            var result = await SynthesizeAsync(synthesisRequest);

            var audioResult = new AudioResult
            {
                Success = true,
                AudioUrl = result.AudioUrl,
                AudioData = result.AudioData,
                DurationMs = result.Duration,
                FromCache = false // Could check if it was from cache
            };
            
            _logger.LogInformation("GenerateSpeechAsync successful - RequestId: {RequestId}, AudioUrl: {Url}, Duration: {Duration}ms",
                requestId, audioResult.AudioUrl, audioResult.DurationMs);
            
            return audioResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GenerateSpeechAsync failed - RequestId: {RequestId}, Voice: {Voice}",
                requestId, request.Voice);
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