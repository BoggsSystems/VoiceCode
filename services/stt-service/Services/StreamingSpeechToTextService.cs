using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text.Json;
using VoiceCode.Common.Interfaces;
using VoiceCode.Common.Models;
using VoiceCode.STTService.Configuration;

namespace VoiceCode.STTService.Services;

public interface IStreamingSTTService
{
    Task<string> StartStreamingSessionAsync(string sessionId, string language = "en-US");
    Task<TranscriptionResult> ProcessAudioChunkAsync(string sessionId, byte[] audioData);
    Task<TranscriptionResult> EndStreamingSessionAsync(string sessionId);
    void CancelStreamingSession(string sessionId);
}

public class StreamingSpeechToTextService : IStreamingSTTService, IDisposable
{
    private readonly AzureSpeechOptions _options;
    private readonly ILogger<StreamingSpeechToTextService> _logger;
    private readonly SpeechConfig _speechConfig;
    private readonly ConcurrentDictionary<string, StreamingSession> _sessions;

    public StreamingSpeechToTextService(
        IOptions<AzureSpeechOptions> options,
        ILogger<StreamingSpeechToTextService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _sessions = new ConcurrentDictionary<string, StreamingSession>();

        // Configure speech service
        if (_options.UseCustomEndpoint && !string.IsNullOrEmpty(_options.Endpoint))
        {
            _speechConfig = SpeechConfig.FromEndpoint(new Uri(_options.Endpoint), _options.Key);
        }
        else
        {
            _speechConfig = SpeechConfig.FromSubscription(_options.Key, _options.Region);
        }

        // Enable continuous recognition
        _speechConfig.OutputFormat = OutputFormat.Detailed;
        _speechConfig.SetProperty("SPEECH-PhraseMode", "Interactive");
        _speechConfig.SetProperty("SPEECH-SyncRecognizeTimeout", "10000");
    }

    public async Task<string> StartStreamingSessionAsync(string sessionId, string language = "en-US")
    {
        _logger.LogInformation("Starting streaming session {SessionId} for language {Language}", sessionId, language);

        if (_sessions.ContainsKey(sessionId))
        {
            _logger.LogWarning("Session {SessionId} already exists", sessionId);
            return sessionId;
        }

        var session = new StreamingSession
        {
            SessionId = sessionId,
            Language = language,
            StartTime = DateTime.UtcNow,
            PushStream = AudioInputStream.CreatePushStream(),
            PartialResults = new List<string>(),
            FinalResults = new List<string>()
        };

        // Configure speech recognition
        _speechConfig.SpeechRecognitionLanguage = language;
        
        var audioConfig = AudioConfig.FromStreamInput(session.PushStream);
        session.Recognizer = new SpeechRecognizer(_speechConfig, audioConfig);

        // Configure continuous recognition
        session.Recognizer.Properties.SetProperty(
            PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs, "1000");
        session.Recognizer.Properties.SetProperty(
            PropertyId.SpeechServiceConnection_InitialSilenceTimeoutMs, "5000");

        // Setup event handlers
        session.Recognizer.Recognizing += (s, e) =>
        {
            if (e.Result.Reason == ResultReason.RecognizingSpeech)
            {
                session.LastPartialResult = e.Result.Text;
                session.PartialResults.Add(e.Result.Text);
                _logger.LogDebug("Partial result for {SessionId}: {Text}", sessionId, e.Result.Text);
            }
        };

        session.Recognizer.Recognized += (s, e) =>
        {
            if (e.Result.Reason == ResultReason.RecognizedSpeech)
            {
                session.FinalResults.Add(e.Result.Text);
                session.LastFinalResult = e.Result.Text;
                session.LastConfidence = ExtractConfidence(e.Result);
                _logger.LogInformation("Final result for {SessionId}: {Text} ({Confidence:P})", 
                    sessionId, e.Result.Text, session.LastConfidence);
            }
        };

        session.Recognizer.SessionStopped += (s, e) =>
        {
            _logger.LogInformation("Session stopped for {SessionId}", sessionId);
            session.IsActive = false;
        };

        session.Recognizer.Canceled += (s, e) =>
        {
            _logger.LogWarning("Recognition canceled for {SessionId}: {Reason}", sessionId, e.Reason);
            session.IsActive = false;
        };

        // Start continuous recognition
        await session.Recognizer.StartContinuousRecognitionAsync();
        session.IsActive = true;

        _sessions[sessionId] = session;
        return sessionId;
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

        // Write audio data to push stream
        session.PushStream.Write(audioData);
        session.TotalBytesProcessed += audioData.Length;

        // Return current state
        var result = new TranscriptionResult
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = sessionId,
            Transcript = session.LastPartialResult ?? string.Empty,
            IsFinal = false,
            Confidence = session.LastConfidence,
            Language = session.Language,
            DurationMs = (long)(DateTime.UtcNow - session.StartTime).TotalMilliseconds
        };

        // If we have a final result since last check, return it
        if (!string.IsNullOrEmpty(session.LastFinalResult) && 
            session.LastFinalResult != session.LastReturnedFinalResult)
        {
            result.Transcript = session.LastFinalResult;
            result.IsFinal = true;
            session.LastReturnedFinalResult = session.LastFinalResult;
        }

        return result;
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

            // Combine all final results
            var fullTranscript = string.Join(" ", session.FinalResults);
            
            var result = new TranscriptionResult
            {
                Id = Guid.NewGuid().ToString(),
                SessionId = sessionId,
                Transcript = fullTranscript,
                IsFinal = true,
                Confidence = session.LastConfidence,
                Language = session.Language,
                DurationMs = (long)(DateTime.UtcNow - session.StartTime).TotalMilliseconds,
                AudioSizeBytes = session.TotalBytesProcessed
            };

            _logger.LogInformation("Ended streaming session {SessionId}: {WordCount} words, {Duration}ms", 
                sessionId, fullTranscript.Split(' ').Length, result.DurationMs);

            return result;
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

        return 0.85;
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

    private class StreamingSession : IDisposable
    {
        public string SessionId { get; set; } = string.Empty;
        public string Language { get; set; } = "en-US";
        public DateTime StartTime { get; set; }
        public PushAudioInputStream PushStream { get; set; } = null!;
        public SpeechRecognizer Recognizer { get; set; } = null!;
        public bool IsActive { get; set; }
        public List<string> PartialResults { get; set; } = new();
        public List<string> FinalResults { get; set; } = new();
        public string? LastPartialResult { get; set; }
        public string? LastFinalResult { get; set; }
        public string? LastReturnedFinalResult { get; set; }
        public double LastConfidence { get; set; } = 0.85;
        public long TotalBytesProcessed { get; set; }

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
}