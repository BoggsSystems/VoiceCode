using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Options;
using System.Text.Json;
using VoiceCode.Common.Interfaces;
using VoiceCode.Common.Models;
using VoiceCode.STTService.Configuration;
using Polly;
using Polly.Retry;

namespace VoiceCode.STTService.Services;

public class SpeechToTextService : ISTTService
{
    private readonly AzureSpeechOptions _options;
    private readonly ILogger<SpeechToTextService> _logger;
    private readonly SpeechConfig _speechConfig;
    private readonly AsyncRetryPolicy _retryPolicy;

    public SpeechToTextService(
        IOptions<AzureSpeechOptions> options,
        ILogger<SpeechToTextService> logger)
    {
        _options = options.Value;
        _logger = logger;

        // Configure speech service
        if (_options.UseCustomEndpoint && !string.IsNullOrEmpty(_options.Endpoint))
        {
            _speechConfig = SpeechConfig.FromEndpoint(new Uri(_options.Endpoint), _options.Key);
        }
        else
        {
            _speechConfig = SpeechConfig.FromSubscription(_options.Key, _options.Region);
        }

        // Configure retry policy
        _retryPolicy = Policy
            .Handle<Exception>(ex => !(ex is InvalidOperationException))
            .WaitAndRetryAsync(
                3,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    _logger.LogWarning("Retry {RetryCount} after {Delay}ms", retryCount, timespan.TotalMilliseconds);
                });
    }

    public async Task<TranscriptionResult> TranscribeAsync(byte[] audioData, string language = "en-US")
    {
        using var activity = System.Diagnostics.Activity.Current;
        activity?.SetTag("language", language);
        activity?.SetTag("audio_size", audioData.Length);

        return await _retryPolicy.ExecuteAsync(async () =>
        {
            _logger.LogInformation("Starting transcription for {ByteCount} bytes of audio in {Language}", 
                audioData.Length, language);

            var result = new TranscriptionResult
            {
                Id = Guid.NewGuid().ToString(),
                Language = language
            };

            try
            {
                _speechConfig.SpeechRecognitionLanguage = language;

                // Enable detailed results for better accuracy
                _speechConfig.OutputFormat = OutputFormat.Detailed;
                _speechConfig.SetProperty(PropertyId.SpeechServiceConnection_SingleLanguageIdPriority, "Latency");

                using var audioStream = new MemoryStream(audioData);
                using var audioConfig = AudioConfig.FromStreamInput(AudioInputStream.CreatePushStream());
                using var recognizer = new SpeechRecognizer(_speechConfig, audioConfig);

                // Configure recognition settings
                recognizer.Properties.SetProperty(
                    PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs,
                    "3000");

                // Create push stream and write audio data
                using var pushStream = AudioInputStream.CreatePushStream();
                using var audioInput = AudioConfig.FromStreamInput(pushStream);
                
                // Write audio data to stream
                pushStream.Write(audioData);
                pushStream.Close();

                // Create recognizer with the audio input
                using var streamRecognizer = new SpeechRecognizer(_speechConfig, audioInput);

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                // Perform recognition
                var recognitionResult = await streamRecognizer.RecognizeOnceAsync();
                
                stopwatch.Stop();
                result.DurationMs = stopwatch.ElapsedMilliseconds;

                switch (recognitionResult.Reason)
                {
                    case ResultReason.RecognizedSpeech:
                        result.Transcript = recognitionResult.Text;
                        result.Confidence = ExtractConfidence(recognitionResult);
                        result.Words = ExtractWords(recognitionResult);
                        result.Alternatives = ExtractAlternatives(recognitionResult);
                        
                        _logger.LogInformation("Transcription successful: {TranscriptLength} characters, {Confidence:P} confidence",
                            result.Transcript.Length, result.Confidence);
                        
                        activity?.SetTag("transcription.success", true);
                        activity?.SetTag("transcription.confidence", result.Confidence);
                        break;

                    case ResultReason.NoMatch:
                        _logger.LogWarning("No speech could be recognized");
                        result.Transcript = string.Empty;
                        result.Confidence = 0;
                        activity?.SetTag("transcription.success", false);
                        activity?.SetTag("transcription.reason", "NoMatch");
                        break;

                    case ResultReason.Canceled:
                        var cancellation = CancellationDetails.FromResult(recognitionResult);
                        _logger.LogError("Recognition canceled: {Reason} - {ErrorDetails}", 
                            cancellation.Reason, cancellation.ErrorDetails);
                        
                        activity?.SetTag("transcription.success", false);
                        activity?.SetTag("transcription.error", cancellation.ErrorDetails);
                        
                        throw new InvalidOperationException($"Recognition canceled: {cancellation.ErrorDetails}");

                    default:
                        _logger.LogError("Unexpected recognition result: {Reason}", recognitionResult.Reason);
                        throw new InvalidOperationException($"Unexpected recognition result: {recognitionResult.Reason}");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Transcription failed");
                activity?.SetTag("transcription.error", ex.Message);
                throw;
            }
        });
    }

    public Task<List<string>> GetSupportedLanguagesAsync()
    {
        // List of languages supported by Azure Speech Service
        var supportedLanguages = new List<string>
        {
            "en-US", "en-GB", "en-AU", "en-CA", "en-IN",
            "es-ES", "es-MX", "es-AR",
            "fr-FR", "fr-CA",
            "de-DE", "de-AT", "de-CH",
            "it-IT",
            "pt-BR", "pt-PT",
            "zh-CN", "zh-TW", "zh-HK",
            "ja-JP",
            "ko-KR",
            "ru-RU",
            "nl-NL",
            "sv-SE",
            "da-DK",
            "no-NO",
            "fi-FI",
            "pl-PL",
            "tr-TR",
            "ar-SA", "ar-EG",
            "hi-IN",
            "th-TH",
            "cs-CZ",
            "hu-HU",
            "ro-RO",
            "sk-SK",
            "uk-UA",
            "vi-VN",
            "id-ID",
            "ms-MY",
            "bg-BG",
            "hr-HR",
            "sl-SI",
            "he-IL"
        };

        return Task.FromResult(supportedLanguages);
    }

    private double ExtractConfidence(SpeechRecognitionResult result)
    {
        try
        {
            var json = result.Properties.GetProperty(PropertyId.SpeechServiceResponse_JsonResult);
            if (!string.IsNullOrEmpty(json))
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("NBest", out var nBest) && 
                    nBest.GetArrayLength() > 0)
                {
                    var firstResult = nBest[0];
                    if (firstResult.TryGetProperty("Confidence", out var confidence))
                    {
                        return confidence.GetDouble();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract confidence score");
        }

        // Default confidence if we can't extract it
        return 0.85;
    }

    private List<TranscriptionAlternative> ExtractAlternatives(SpeechRecognitionResult result)
    {
        var alternatives = new List<TranscriptionAlternative>();

        try
        {
            var json = result.Properties.GetProperty(PropertyId.SpeechServiceResponse_JsonResult);
            if (!string.IsNullOrEmpty(json))
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("NBest", out var nBest))
                {
                    // Skip the first one as it's the main result
                    foreach (var item in nBest.EnumerateArray().Skip(1).Take(3))
                    {
                        if (item.TryGetProperty("Display", out var display) &&
                            item.TryGetProperty("Confidence", out var confidence))
                        {
                            alternatives.Add(new TranscriptionAlternative
                            {
                                Transcript = display.GetString() ?? string.Empty,
                                Confidence = confidence.GetDouble()
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract alternatives");
        }

        return alternatives;
    }

    private List<TranscriptionWord> ExtractWords(SpeechRecognitionResult result)
    {
        var words = new List<TranscriptionWord>();

        try
        {
            var json = result.Properties.GetProperty(PropertyId.SpeechServiceResponse_JsonResult);
            if (!string.IsNullOrEmpty(json))
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("NBest", out var nBest) && 
                    nBest.GetArrayLength() > 0)
                {
                    var firstResult = nBest[0];
                    if (firstResult.TryGetProperty("Words", out var wordsArray))
                    {
                        foreach (var wordElement in wordsArray.EnumerateArray())
                        {
                            if (wordElement.TryGetProperty("Word", out var word) &&
                                wordElement.TryGetProperty("Offset", out var offset) &&
                                wordElement.TryGetProperty("Duration", out var duration))
                            {
                                // Convert 100-nanosecond units to milliseconds
                                var startTime = offset.GetInt64() / 10000.0;
                                var durationMs = duration.GetInt64() / 10000.0;

                                words.Add(new TranscriptionWord
                                {
                                    Word = word.GetString() ?? string.Empty,
                                    StartTime = startTime,
                                    EndTime = startTime + durationMs,
                                    Confidence = wordElement.TryGetProperty("Confidence", out var conf) 
                                        ? conf.GetDouble() 
                                        : 1.0
                                });
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract word timings");
        }

        return words;
    }
}