using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using VoiceCode.Common.Interfaces;
using VoiceCode.Common.Models;
using VoiceCode.STTService.Configuration;

namespace VoiceCode.STTService.Services;

public interface IEnhancedStreamingSTTService : IStreamingSTTService
{
    Task<StreamingSessionInfo> GetSessionInfoAsync(string sessionId);
    Task<List<TranscriptionSegment>> GetSessionTranscriptsAsync(string sessionId);
    Task<bool> IsSessionActiveAsync(string sessionId);
    Task<StreamingMetrics> GetSessionMetricsAsync(string sessionId);
}

public class EnhancedStreamingSTTService : IEnhancedStreamingSTTService, IDisposable
{
    private readonly AzureSpeechOptions _options;
    private readonly ILogger<EnhancedStreamingSTTService> _logger;
    private readonly SpeechConfig _speechConfig;
    private readonly ConcurrentDictionary<string, EnhancedStreamingSession> _sessions;
    private readonly ITranscriptionStreamManager _streamManager;

    public EnhancedStreamingSTTService(
        IOptions<AzureSpeechOptions> options,
        ILogger<EnhancedStreamingSTTService> logger,
        ITranscriptionStreamManager streamManager)
    {
        _options = options.Value;
        _logger = logger;
        _streamManager = streamManager;
        _sessions = new ConcurrentDictionary<string, EnhancedStreamingSession>();

        // Configure speech service
        if (_options.UseCustomEndpoint && !string.IsNullOrEmpty(_options.Endpoint))
        {
            _speechConfig = SpeechConfig.FromEndpoint(new Uri(_options.Endpoint), _options.Key);
        }
        else
        {
            _speechConfig = SpeechConfig.FromSubscription(_options.Key, _options.Region);
        }

        // Enhanced configuration for streaming
        _speechConfig.OutputFormat = OutputFormat.Detailed;
        _speechConfig.SetProperty("SPEECH-PhraseMode", "Interactive");
        _speechConfig.SetProperty("SPEECH-SyncRecognizeTimeout", "10000");
        _speechConfig.SetProperty("SPEECH-RecoBackend", "Online");
        _speechConfig.EnableDictation();
        
        // Enable word-level timestamps
        _speechConfig.RequestWordLevelTimestamps();
    }

    public async Task<string> StartStreamingSessionAsync(string sessionId, string language = "en-US")
    {
        _logger.LogInformation("Starting enhanced streaming session {SessionId} for language {Language}", sessionId, language);

        if (_sessions.ContainsKey(sessionId))
        {
            _logger.LogWarning("Session {SessionId} already exists, returning existing session", sessionId);
            return sessionId;
        }

        var session = new EnhancedStreamingSession
        {
            SessionId = sessionId,
            Language = language,
            StartTime = DateTime.UtcNow,
            PushStream = AudioInputStream.CreatePushStream(AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1)),
            TranscriptionBuffer = new TranscriptionBuffer(sessionId),
            Metrics = new StreamingMetrics { SessionId = sessionId }
        };

        // Configure speech recognition
        _speechConfig.SpeechRecognitionLanguage = language;
        
        var audioConfig = AudioConfig.FromStreamInput(session.PushStream);
        session.Recognizer = new SpeechRecognizer(_speechConfig, audioConfig);

        // Configure enhanced recognition properties
        session.Recognizer.Properties.SetProperty(
            PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs, "800");
        session.Recognizer.Properties.SetProperty(
            PropertyId.SpeechServiceConnection_InitialSilenceTimeoutMs, "3000");
        // Note: SegmentationSilenceTimeoutMs is not available in current SDK version
        // session.Recognizer.Properties.SetProperty(
        //     PropertyId.SpeechServiceConnection_SegmentationSilenceTimeoutMs, "300");

        // Setup enhanced event handlers
        SetupEventHandlers(session);

        // Start continuous recognition
        await session.Recognizer.StartContinuousRecognitionAsync();
        session.IsActive = true;
        session.Metrics.SessionStartTime = DateTime.UtcNow;

        _sessions[sessionId] = session;
        await _streamManager.RegisterStreamAsync(sessionId, session);

        return sessionId;
    }

    private void SetupEventHandlers(EnhancedStreamingSession session)
    {
        // Partial results handler
        session.Recognizer.Recognizing += (s, e) =>
        {
            if (e.Result.Reason == ResultReason.RecognizingSpeech)
            {
                var partialResult = new PartialTranscriptionResult
                {
                    Text = e.Result.Text,
                    Offset = e.Result.OffsetInTicks,
                    Duration = e.Result.Duration.Ticks,
                    Timestamp = DateTime.UtcNow
                };

                session.TranscriptionBuffer.AddPartial(partialResult);
                session.Metrics.PartialResultCount++;

                // Notify listeners
                OnPartialResult?.Invoke(session.SessionId, partialResult);

                _logger.LogDebug("Partial result for {SessionId}: {Text} at offset {Offset}",
                    session.SessionId, e.Result.Text, e.Result.OffsetInTicks);
            }
        };

        // Final results handler
        session.Recognizer.Recognized += (s, e) =>
        {
            if (e.Result.Reason == ResultReason.RecognizedSpeech)
            {
                var finalResult = ExtractDetailedResult(e.Result);
                finalResult.SessionId = session.SessionId;
                
                session.TranscriptionBuffer.AddFinal(finalResult);
                session.LastFinalResult = finalResult;
                session.Metrics.FinalResultCount++;
                session.Metrics.TotalWords += finalResult.Words?.Count ?? 0;

                // Notify listeners
                OnFinalResult?.Invoke(session.SessionId, finalResult);

                _logger.LogInformation("Final result for {SessionId}: {Text} ({WordCount} words, {Confidence:P})",
                    session.SessionId, finalResult.Text, finalResult.Words?.Count ?? 0, finalResult.Confidence);
            }
            else if (e.Result.Reason == ResultReason.NoMatch)
            {
                session.Metrics.NoMatchCount++;
                _logger.LogWarning("No match for session {SessionId}", session.SessionId);
            }
        };

        // Session stopped handler
        session.Recognizer.SessionStopped += (s, e) =>
        {
            session.IsActive = false;
            session.Metrics.SessionEndTime = DateTime.UtcNow;
            _logger.LogInformation("Session stopped for {SessionId}, duration: {Duration}",
                session.SessionId, session.Metrics.SessionDuration);
        };

        // Canceled handler with detailed error info
        session.Recognizer.Canceled += (s, e) =>
        {
            session.IsActive = false;
            session.LastError = CancellationDetails.FromResult(e.Result);
            session.Metrics.ErrorCount++;
            
            _logger.LogError("Recognition canceled for {SessionId}: {Reason} - {ErrorDetails}",
                session.SessionId, e.Reason, session.LastError?.ErrorDetails);
            
            OnError?.Invoke(session.SessionId, session.LastError);
        };

        // Speech detected events
        session.Recognizer.SpeechStartDetected += (s, e) =>
        {
            session.Metrics.SpeechStartCount++;
            _logger.LogDebug("Speech start detected for {SessionId} at offset {Offset}",
                session.SessionId, e.Offset);
        };

        session.Recognizer.SpeechEndDetected += (s, e) =>
        {
            session.Metrics.SpeechEndCount++;
            _logger.LogDebug("Speech end detected for {SessionId} at offset {Offset}",
                session.SessionId, e.Offset);
        };
    }

    public async Task<TranscriptionResult> ProcessAudioChunkAsync(string sessionId, byte[] audioData)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        if (!session.IsActive)
        {
            throw new InvalidOperationException($"Session {sessionId} is not active");
        }

        try
        {
            // Write audio data to push stream
            session.PushStream.Write(audioData);
            session.Metrics.TotalBytesProcessed += audioData.Length;
            session.Metrics.ChunksProcessed++;

            // Get current transcription state
            var currentState = session.TranscriptionBuffer.GetCurrentState();
            
            return new TranscriptionResult
            {
                Id = Guid.NewGuid().ToString(),
                SessionId = sessionId,
                Text = currentState.CurrentPartial ?? currentState.LastFinal ?? string.Empty,
                IsFinal = false,
                Confidence = currentState.AverageConfidence,
                Language = session.Language,
                DurationMs = (long)(DateTime.UtcNow - session.StartTime).TotalMilliseconds,
                AudioSizeBytes = session.Metrics.TotalBytesProcessed,
                Words = currentState.AccumulatedWords,
                Metadata = new Dictionary<string, object>
                {
                    ["chunkIndex"] = session.Metrics.ChunksProcessed,
                    ["partialCount"] = session.Metrics.PartialResultCount,
                    ["finalCount"] = session.Metrics.FinalResultCount
                }
            };
        }
        catch (Exception ex)
        {
            session.Metrics.ErrorCount++;
            _logger.LogError(ex, "Error processing audio chunk for session {SessionId}", sessionId);
            throw;
        }
    }

    public async Task<TranscriptionResult> EndStreamingSessionAsync(string sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out var session))
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        try
        {
            // Close the push stream to signal end of audio
            session.PushStream.Close();

            // Stop continuous recognition
            if (session.IsActive)
            {
                await session.Recognizer.StopContinuousRecognitionAsync();
            }

            // Get final aggregated results
            var aggregatedResult = session.TranscriptionBuffer.GetAggregatedResult();
            aggregatedResult.DurationMs = (long)(DateTime.UtcNow - session.StartTime).TotalMilliseconds;
            aggregatedResult.AudioSizeBytes = session.Metrics.TotalBytesProcessed;
            
            // Update metrics
            session.Metrics.SessionEndTime = DateTime.UtcNow;
            
            // Unregister from stream manager
            await _streamManager.UnregisterStreamAsync(sessionId);

            _logger.LogInformation("Ended streaming session {SessionId}: {WordCount} words, {Duration}ms, {Confidence:P}",
                sessionId, aggregatedResult.Words?.Count ?? 0, aggregatedResult.DurationMs, aggregatedResult.Confidence);

            return aggregatedResult;
        }
        finally
        {
            session.Dispose();
        }
    }

    public void CancelStreamingSession(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            try
            {
                session.PushStream.Close();
                if (session.IsActive)
                {
                    session.Recognizer.StopContinuousRecognitionAsync().Wait(TimeSpan.FromSeconds(5));
                }
                _streamManager.UnregisterStreamAsync(sessionId).Wait();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error canceling session {SessionId}", sessionId);
            }
            finally
            {
                session.Dispose();
            }
        }
    }

    public async Task<StreamingSessionInfo> GetSessionInfoAsync(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return null;
        }

        return new StreamingSessionInfo
        {
            SessionId = sessionId,
            Language = session.Language,
            StartTime = session.StartTime,
            IsActive = session.IsActive,
            Metrics = session.Metrics,
            CurrentTranscript = session.TranscriptionBuffer.GetCurrentState().GetFullTranscript()
        };
    }

    public async Task<List<TranscriptionSegment>> GetSessionTranscriptsAsync(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return new List<TranscriptionSegment>();
        }

        return session.TranscriptionBuffer.GetAllSegments();
    }

    public async Task<bool> IsSessionActiveAsync(string sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var session) && session.IsActive;
    }

    public async Task<StreamingMetrics> GetSessionMetricsAsync(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return null;
        }

        return session.Metrics;
    }

    private TranscriptionResult ExtractDetailedResult(SpeechRecognitionResult result)
    {
        try
        {
            var json = result.Properties.GetProperty(PropertyId.SpeechServiceResponse_JsonResult);
            if (!string.IsNullOrEmpty(json))
            {
                using var doc = JsonDocument.Parse(json);
                return ParseDetailedResult(doc.RootElement, result);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract detailed result");
        }

        // Fallback to basic result
        return new TranscriptionResult
        {
            Id = Guid.NewGuid().ToString(),
            Text = result.Text,
            Confidence = 0.9,
            IsFinal = true,
            Duration = result.Duration.TotalMilliseconds
        };
    }

    private TranscriptionResult ParseDetailedResult(JsonElement root, SpeechRecognitionResult result)
    {
        var transcriptionResult = new TranscriptionResult
        {
            Id = Guid.NewGuid().ToString(),
            Text = result.Text,
            IsFinal = true,
            Duration = result.Duration.TotalMilliseconds
        };

        if (root.TryGetProperty("NBest", out var nBest) && nBest.GetArrayLength() > 0)
        {
            var firstResult = nBest[0];
            
            if (firstResult.TryGetProperty("Confidence", out var confidence))
            {
                transcriptionResult.Confidence = confidence.GetDouble();
            }

            if (firstResult.TryGetProperty("Words", out var words))
            {
                transcriptionResult.Words = ParseWords(words);
            }

            // Get alternatives
            if (nBest.GetArrayLength() > 1)
            {
                transcriptionResult.Alternatives = new List<TranscriptionAlternative>();
                for (int i = 1; i < Math.Min(nBest.GetArrayLength(), 4); i++)
                {
                    var alt = nBest[i];
                    if (alt.TryGetProperty("Display", out var display) &&
                        alt.TryGetProperty("Confidence", out var altConf))
                    {
                        transcriptionResult.Alternatives.Add(new TranscriptionAlternative
                        {
                            Text = display.GetString(),
                            Confidence = altConf.GetDouble()
                        });
                    }
                }
            }
        }

        return transcriptionResult;
    }

    private List<TranscriptionWord> ParseWords(JsonElement wordsArray)
    {
        var words = new List<TranscriptionWord>();
        
        foreach (var wordElement in wordsArray.EnumerateArray())
        {
            if (wordElement.TryGetProperty("Word", out var word) &&
                wordElement.TryGetProperty("Offset", out var offset) &&
                wordElement.TryGetProperty("Duration", out var duration))
            {
                var startTime = offset.GetInt64() / 10000.0; // Convert to ms
                var durationMs = duration.GetInt64() / 10000.0;

                words.Add(new TranscriptionWord
                {
                    Word = word.GetString(),
                    StartTime = startTime,
                    EndTime = startTime + durationMs,
                    Confidence = wordElement.TryGetProperty("Confidence", out var conf) 
                        ? conf.GetDouble() 
                        : 1.0
                });
            }
        }

        return words;
    }

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
        {
            try
            {
                session.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing session {SessionId}", session.SessionId);
            }
        }
        _sessions.Clear();
    }

    // Events for external listeners
    public event Action<string, PartialTranscriptionResult> OnPartialResult;
    public event Action<string, TranscriptionResult> OnFinalResult;
    public event Action<string, CancellationDetails> OnError;
}

public class EnhancedStreamingSession : IDisposable
{
    public string SessionId { get; set; }
    public string Language { get; set; }
    public DateTime StartTime { get; set; }
    public PushAudioInputStream PushStream { get; set; }
    public SpeechRecognizer Recognizer { get; set; }
    public bool IsActive { get; set; }
    public TranscriptionBuffer TranscriptionBuffer { get; set; }
    public TranscriptionResult LastFinalResult { get; set; }
    public CancellationDetails LastError { get; set; }
    public StreamingMetrics Metrics { get; set; }

    public void Dispose()
    {
        try
        {
            PushStream?.Dispose();
            Recognizer?.Dispose();
        }
        catch { }
    }
}

public class StreamingSessionInfo
{
    public string SessionId { get; set; }
    public string Language { get; set; }
    public DateTime StartTime { get; set; }
    public bool IsActive { get; set; }
    public StreamingMetrics Metrics { get; set; }
    public string CurrentTranscript { get; set; }
}

public class StreamingMetrics
{
    public string SessionId { get; set; }
    public DateTime SessionStartTime { get; set; }
    public DateTime? SessionEndTime { get; set; }
    public long TotalBytesProcessed { get; set; }
    public int ChunksProcessed { get; set; }
    public int PartialResultCount { get; set; }
    public int FinalResultCount { get; set; }
    public int NoMatchCount { get; set; }
    public int ErrorCount { get; set; }
    public int SpeechStartCount { get; set; }
    public int SpeechEndCount { get; set; }
    public int TotalWords { get; set; }
    
    public TimeSpan SessionDuration => 
        (SessionEndTime ?? DateTime.UtcNow) - SessionStartTime;
    
    public double AverageChunkSize => 
        ChunksProcessed > 0 ? (double)TotalBytesProcessed / ChunksProcessed : 0;
    
    public double WordsPerMinute => 
        SessionDuration.TotalMinutes > 0 ? TotalWords / SessionDuration.TotalMinutes : 0;
}

public class PartialTranscriptionResult
{
    public string Text { get; set; }
    public long Offset { get; set; }
    public long Duration { get; set; }
    public DateTime Timestamp { get; set; }
}

// TranscriptionSegment is now defined in VoiceCode.Common.Models