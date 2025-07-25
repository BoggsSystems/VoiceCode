using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using VoiceCode.DispatcherService.Models;
using VoiceCode.DispatcherService.Services;
using VoiceCode.Common.Models;
using VoiceCode.Common.DTOs;
using System.Text.Json;
using System.Linq;

namespace VoiceCode.DispatcherService.Hubs
{
    [Authorize]
    public class AudioStreamHub : Hub
    {
        private readonly ILogger<AudioStreamHub> _logger;
        private readonly ISessionManager _sessionManager;
        private readonly IServiceRouter _serviceRouter;
        private readonly IQueueDispatcher _queueDispatcher;
        private readonly IVADService _vadService;
        private static readonly ConcurrentDictionary<string, AudioStreamSession> _streamSessions = new();
        private static readonly ConcurrentDictionary<string, Timer> _heartbeatTimers = new();

        public AudioStreamHub(
            ILogger<AudioStreamHub> logger,
            ISessionManager sessionManager,
            IServiceRouter serviceRouter,
            IQueueDispatcher queueDispatcher,
            IVADService vadService)
        {
            _logger = logger;
            _sessionManager = sessionManager;
            _serviceRouter = serviceRouter;
            _queueDispatcher = queueDispatcher;
            _vadService = vadService;
        }

        public override async Task OnConnectedAsync()
        {
            var userSession = await _sessionManager.CreateSessionAsync(Context.UserIdentifier, Context.ConnectionId);
            
            var streamSession = new AudioStreamSession
            {
                SessionId = userSession.Id,
                ConnectionId = Context.ConnectionId,
                UserId = Context.UserIdentifier,
                StartTime = DateTime.UtcNow,
                Config = new AudioStreamConfig()
            };

            _streamSessions.TryAdd(Context.ConnectionId, streamSession);
            
            await Groups.AddToGroupAsync(Context.ConnectionId, userSession.Id);
            
            StartHeartbeat(Context.ConnectionId);
            
            await Clients.Caller.SendAsync("StreamSessionStarted", new
            {
                sessionId = userSession.Id,
                config = streamSession.Config,
                timestamp = DateTime.UtcNow
            });

            _logger.LogInformation("Audio stream session started: {SessionId} for user {UserId}", 
                userSession.Id, Context.UserIdentifier);
            
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            if (_streamSessions.TryRemove(Context.ConnectionId, out var session))
            {
                StopHeartbeat(Context.ConnectionId);
                
                if (session.IsStreaming)
                {
                    await StopAudioStream();
                }
                
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, session.SessionId);
                
                _logger.LogInformation("Audio stream session ended: {SessionId}, Duration: {Duration}",
                    session.SessionId, DateTime.UtcNow - session.StartTime);
            }
            
            await base.OnDisconnectedAsync(exception);
        }

        public async Task StartAudioStream(AudioStreamConfig config = null)
        {
            if (!_streamSessions.TryGetValue(Context.ConnectionId, out var session))
            {
                await Clients.Caller.SendAsync("StreamError", new ErrorPayload
                {
                    ErrorCode = "SESSION_NOT_FOUND",
                    Message = "No active session found",
                    IsRecoverable = false
                });
                return;
            }

            if (session.IsStreaming)
            {
                await Clients.Caller.SendAsync("StreamError", new ErrorPayload
                {
                    ErrorCode = "ALREADY_STREAMING",
                    Message = "Audio stream already active",
                    IsRecoverable = true
                });
                return;
            }

            if (config != null)
            {
                session.Config = config;
            }

            session.IsStreaming = true;
            session.StreamStartTime = DateTime.UtcNow;
            session.LastSequenceNumber = 0;
            session.AudioBuffer.Clear();

            await Clients.Caller.SendAsync("StreamStarted", new
            {
                sessionId = session.SessionId,
                timestamp = DateTime.UtcNow,
                config = session.Config
            });

            _logger.LogInformation("Audio streaming started for session: {SessionId}", session.SessionId);
        }

        public async Task StopAudioStream()
        {
            if (!_streamSessions.TryGetValue(Context.ConnectionId, out var session))
            {
                return;
            }

            if (!session.IsStreaming)
            {
                return;
            }

            session.IsStreaming = false;
            
            if (session.AudioBuffer.Count > 0)
            {
                await ProcessBufferedAudio(session);
            }

            await Clients.Caller.SendAsync("StreamStopped", new
            {
                sessionId = session.SessionId,
                timestamp = DateTime.UtcNow,
                duration = DateTime.UtcNow - session.StreamStartTime,
                totalChunks = session.LastSequenceNumber
            });

            _logger.LogInformation("Audio streaming stopped for session: {SessionId}, Total chunks: {Chunks}",
                session.SessionId, session.LastSequenceNumber);
        }

        public async Task SendAudioChunk(string messageJson)
        {
            try
            {
                var message = JsonSerializer.Deserialize<AudioStreamMessage>(messageJson);
                
                if (!_streamSessions.TryGetValue(Context.ConnectionId, out var session))
                {
                    await SendError("SESSION_NOT_FOUND", "No active session found", false);
                    return;
                }

                if (!session.IsStreaming)
                {
                    await SendError("NOT_STREAMING", "Audio stream not active", true);
                    return;
                }

                switch (message.Type)
                {
                    case AudioStreamMessageType.AudioData:
                        await ProcessAudioData(session, message);
                        break;
                    case AudioStreamMessageType.Control:
                        await ProcessControlMessage(session, message);
                        break;
                    case AudioStreamMessageType.Metadata:
                        await ProcessMetadata(session, message);
                        break;
                    case AudioStreamMessageType.Heartbeat:
                        await ProcessHeartbeat(session, message);
                        break;
                    default:
                        _logger.LogWarning("Unknown message type: {Type}", message.Type);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing audio chunk");
                await SendError("PROCESSING_ERROR", "Failed to process audio chunk", true);
            }
        }

        private async Task ProcessAudioData(AudioStreamSession session, AudioStreamMessage message)
        {
            var payload = JsonSerializer.Deserialize<AudioDataPayload>(message.Payload.ToString());
            
            session.LastSequenceNumber++;
            
            // Check for VAD metadata from client
            var clientVadEnabled = false;
            var clientVadState = VADState.Idle;
            
            if (payload is AudioDataPayloadWithVAD vadPayload && vadPayload.Metadata != null)
            {
                clientVadEnabled = vadPayload.Metadata.ContainsKey("vadEnabled") && 
                                 (bool)vadPayload.Metadata["vadEnabled"];
                if (vadPayload.Metadata.ContainsKey("vadState"))
                {
                    Enum.TryParse<VADState>(vadPayload.Metadata["vadState"].ToString(), out clientVadState);
                }
            }
            
            // Perform server-side VAD validation if enabled
            if (session.VADEnabled || clientVadEnabled)
            {
                var vadResult = _vadService.ProcessAudioFrame(
                    session.SessionId, 
                    payload.AudioData, 
                    payload.SampleRate);
                
                // Send VAD result to client
                await Clients.Caller.SendAsync("VADResult", new
                {
                    sessionId = session.SessionId,
                    serverState = vadResult.State.ToString(),
                    clientState = clientVadState.ToString(),
                    confidence = vadResult.Confidence,
                    energy = vadResult.Energy,
                    timestamp = DateTime.UtcNow
                });
                
                // Apply false positive reduction
                if (clientVadEnabled && session.VADEnabled)
                {
                    // Only process audio if both client and server agree on speech
                    if (clientVadState == VADState.Speech && vadResult.State == VADState.Speech)
                    {
                        session.AudioBuffer.Add(payload);
                    }
                    else if (vadResult.State == VADState.Silence && session.AudioBuffer.Count > 0)
                    {
                        // Process any buffered audio when silence is detected
                        await ProcessBufferedAudio(session);
                    }
                }
                else
                {
                    // Use server VAD only
                    if (vadResult.State == VADState.Speech || vadResult.State == VADState.MaybeSpeech)
                    {
                        session.AudioBuffer.Add(payload);
                    }
                    else if (vadResult.State == VADState.Silence && session.AudioBuffer.Count > 0)
                    {
                        await ProcessBufferedAudio(session);
                    }
                }
            }
            else
            {
                // No VAD - process all audio
                session.AudioBuffer.Add(payload);
            }
            
            await Clients.Caller.SendAsync("AudioChunkAcknowledged", new AcknowledgmentPayload
            {
                AcknowledgedSequence = payload.SequenceNumber,
                ProcessingLatency = TimeSpan.FromMilliseconds(5)
            });

            // Process buffered audio based on time or buffer size
            if (session.AudioBuffer.Count >= 10 || 
                (DateTime.UtcNow - session.LastProcessTime).TotalMilliseconds >= 1000)
            {
                await ProcessBufferedAudio(session);
            }
        }

        private async Task ProcessBufferedAudio(AudioStreamSession session)
        {
            if (session.AudioBuffer.Count == 0) return;

            var audioChunks = session.AudioBuffer.ToArray();
            session.AudioBuffer.Clear();
            session.LastProcessTime = DateTime.UtcNow;

            var combinedAudio = CombineAudioChunks(audioChunks);
            
            try
            {
                var transcriptionResult = await _serviceRouter.ProcessStreamingAudioAsync(
                    session.SessionId, 
                    combinedAudio,
                    session.Config.SampleRate);

                if (transcriptionResult != null)
                {
                    await Clients.Group(session.SessionId).SendAsync("PartialTranscription", new
                    {
                        sessionId = session.SessionId,
                        text = transcriptionResult.Text,
                        isFinal = transcriptionResult.IsFinal,
                        confidence = transcriptionResult.Confidence,
                        timestamp = DateTime.UtcNow
                    });

                    if (transcriptionResult.IsFinal)
                    {
                        var routingResult = await _serviceRouter.SendToRouterAsync(
                            transcriptionResult.Text, 
                            await _sessionManager.GetSessionAsync(session.SessionId));
                        
                        if (routingResult.Intent.Category == VoiceCode.Common.Enums.IntentCategory.CodeGeneration ||
                            routingResult.Intent.Category == VoiceCode.Common.Enums.IntentCategory.CodeExplanation ||
                            routingResult.Intent.Category == VoiceCode.Common.Enums.IntentCategory.ErrorFixing)
                        {
                            await _queueDispatcher.SendToClaudeAsync(session.SessionId, new
                            {
                                text = transcriptionResult.Text,
                                intent = routingResult.Intent,
                                context = session.Metadata
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing buffered audio for session {SessionId}", session.SessionId);
                await SendError("STT_ERROR", "Failed to process audio", true);
            }
        }

        private byte[] CombineAudioChunks(AudioDataPayload[] chunks)
        {
            var totalSize = chunks.Sum(c => c.AudioData.Length);
            var combined = new byte[totalSize];
            var offset = 0;

            foreach (var chunk in chunks)
            {
                Buffer.BlockCopy(chunk.AudioData, 0, combined, offset, chunk.AudioData.Length);
                offset += chunk.AudioData.Length;
            }

            return combined;
        }

        private async Task ProcessControlMessage(AudioStreamSession session, AudioStreamMessage message)
        {
            var payload = JsonSerializer.Deserialize<ControlPayload>(message.Payload.ToString());
            
            switch (payload.Command)
            {
                case ControlCommand.StartStream:
                    await StartAudioStream();
                    break;
                case ControlCommand.StopStream:
                    await StopAudioStream();
                    break;
                case ControlCommand.PauseStream:
                    session.IsPaused = true;
                    await Clients.Caller.SendAsync("StreamPaused", new { sessionId = session.SessionId });
                    break;
                case ControlCommand.ResumeStream:
                    session.IsPaused = false;
                    await Clients.Caller.SendAsync("StreamResumed", new { sessionId = session.SessionId });
                    break;
                case ControlCommand.ResetStream:
                    session.AudioBuffer.Clear();
                    session.LastSequenceNumber = 0;
                    await Clients.Caller.SendAsync("StreamReset", new { sessionId = session.SessionId });
                    break;
            }
        }

        private async Task ProcessMetadata(AudioStreamSession session, AudioStreamMessage message)
        {
            var payload = JsonSerializer.Deserialize<MetadataPayload>(message.Payload.ToString());
            session.Metadata[payload.Key] = payload.Value;
            
            await Clients.Caller.SendAsync("MetadataUpdated", new
            {
                sessionId = session.SessionId,
                key = payload.Key,
                value = payload.Value
            });
        }

        private async Task ProcessHeartbeat(AudioStreamSession session, AudioStreamMessage message)
        {
            var payload = JsonSerializer.Deserialize<HeartbeatPayload>(message.Payload.ToString());
            
            await Clients.Caller.SendAsync("HeartbeatAck", new HeartbeatPayload
            {
                ClientTimestamp = payload.ClientTimestamp,
                ServerTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Stats = new Dictionary<string, object>
                {
                    ["bufferSize"] = session.AudioBuffer.Count,
                    ["sequenceNumber"] = session.LastSequenceNumber,
                    ["isStreaming"] = session.IsStreaming,
                    ["isPaused"] = session.IsPaused
                }
            });
        }

        private void StartHeartbeat(string connectionId)
        {
            var timer = new Timer(async _ =>
            {
                if (_streamSessions.TryGetValue(connectionId, out var session))
                {
                    var client = Clients.Client(connectionId);
                    await client.SendAsync("Heartbeat", new
                    {
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        sessionId = session.SessionId
                    });
                }
            }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

            _heartbeatTimers[connectionId] = timer;
        }

        private void StopHeartbeat(string connectionId)
        {
            if (_heartbeatTimers.TryRemove(connectionId, out var timer))
            {
                timer?.Dispose();
            }
        }

        private async Task SendError(string code, string message, bool isRecoverable)
        {
            await Clients.Caller.SendAsync("StreamError", new ErrorPayload
            {
                ErrorCode = code,
                Message = message,
                IsRecoverable = isRecoverable,
                Details = null
            });
        }
    }

    public class AudioStreamSession
    {
        public string SessionId { get; set; }
        public string ConnectionId { get; set; }
        public string UserId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime StreamStartTime { get; set; }
        public bool IsStreaming { get; set; }
        public bool IsPaused { get; set; }
        public bool VADEnabled { get; set; }
        public long LastSequenceNumber { get; set; }
        public DateTime LastProcessTime { get; set; }
        public AudioStreamConfig Config { get; set; }
        public ConcurrentBag<AudioDataPayload> AudioBuffer { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}