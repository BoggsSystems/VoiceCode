using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Identity.Web.Resource;
using VoiceCode.Common.Models;
using VoiceCode.Common.Enums;
using VoiceCode.DispatcherService.Services;

namespace VoiceCode.DispatcherService.Hubs;

// Temporarily disabled for debugging SignalR connection issues
// [Authorize]
public class VoiceHub : Hub
{
    private readonly ILogger<VoiceHub> _logger;
    private readonly ISessionManager _sessionManager;
    private readonly IQueueDispatcher _queueDispatcher;
    private readonly IServiceRouter _serviceRouter;
    private readonly IMetricsService _metrics;

    public VoiceHub(
        ILogger<VoiceHub> logger,
        ISessionManager sessionManager,
        IQueueDispatcher queueDispatcher,
        IServiceRouter serviceRouter,
        IMetricsService metrics)
    {
        _logger = logger;
        _sessionManager = sessionManager;
        _queueDispatcher = queueDispatcher;
        _serviceRouter = serviceRouter;
        _metrics = metrics;
    }

    public override async Task OnConnectedAsync()
    {
        try
        {
            // Don't create a session immediately - wait for JoinSession call
            _logger.LogInformation("Client connected: {ConnectionId}, waiting for JoinSession", 
                Context.ConnectionId);
            
            _metrics.RecordConnection();
            
            // Send connection established event
            await Clients.Caller.SendAsync("Connected", new { 
                connectionId = Context.ConnectionId,
                message = "Connected to SignalR hub. Please join a session."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in OnConnectedAsync");
            throw;
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            var session = await _sessionManager.GetSessionByConnectionIdAsync(Context.ConnectionId);
            if (session != null)
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, session.Id);
                await _sessionManager.EndSessionAsync(session.Id);
                
                _logger.LogInformation("Client disconnected: {ConnectionId}, Session: {SessionId}", 
                    Context.ConnectionId, session.Id);
            }

            _metrics.RecordDisconnection();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in OnDisconnectedAsync");
        }

        await base.OnDisconnectedAsync(exception);
    }

    [HubMethodName("ProcessVoiceRequest")]
    public async Task<ProcessingResult> ProcessVoiceRequestAsync(VoiceRequestMessage message)
    {
        try
        {
            _logger.LogInformation("Processing voice request: {RequestId}", message.RequestId);

            // Convert the web app's request format to our internal AudioMessage format
            var audioMessage = new AudioMessage
            {
                Id = message.RequestId,
                AudioData = Convert.FromBase64String(message.AudioData),
                Format = "wav", // Assume WAV format for web app
                SampleRate = 16000,
                Language = message.Language ?? "en-US"
            };

            // Process using the existing audio processing logic
            return await ProcessAudioAsync(audioMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing voice request {RequestId}", message.RequestId);
            
            await Clients.Caller.SendAsync("ProcessingError", new 
            { 
                messageId = message.RequestId, 
                error = ex.Message 
            });

            return new ProcessingResult
            {
                Success = false,
                Error = ex.Message,
                MessageId = message.RequestId
            };
        }
    }

    [HubMethodName("ProcessAudio")]
    public async Task<ProcessingResult> ProcessAudioAsync(AudioMessage message)
    {
        try
        {
            _metrics.RecordMessage("audio", message.AudioData.Length);

            var session = await _sessionManager.GetSessionByConnectionIdAsync(Context.ConnectionId);
            if (session == null)
            {
                throw new InvalidOperationException("No active session found");
            }

            // Update session activity
            await _sessionManager.UpdateSessionActivityAsync(session.Id);

            // Send acknowledgment
            await Clients.Caller.SendAsync("ProcessingStarted", new { messageId = message.Id });

            // Route to STT service
            var transcriptionResult = await _serviceRouter.SendToSTTAsync(message, session.Id);
            
            if (!transcriptionResult.Success)
            {
                return new ProcessingResult
                {
                    Success = false,
                    Error = transcriptionResult.Error,
                    MessageId = message.Id
                };
            }

            // Send transcription update
            await Clients.Group(session.Id).SendAsync("TranscriptionReceived", transcriptionResult);

            // Route to intent classification
            var routingResult = await _serviceRouter.SendToRouterAsync(
                transcriptionResult.Transcript!, 
                session);

            // Queue for processing
            await _queueDispatcher.DispatchAsync(new QueueMessage
            {
                SessionId = session.Id,
                MessageId = message.Id,
                Type = routingResult.Intent.Category.ToString(),
                Payload = new
                {
                    Transcript = transcriptionResult.Transcript,
                    Intent = routingResult,
                    Context = session.Context
                },
                Priority = routingResult.Priority,
                Timestamp = DateTime.UtcNow
            });

            return new ProcessingResult
            {
                Success = true,
                MessageId = message.Id,
                TranscriptionId = transcriptionResult.Id,
                Intent = routingResult.Intent.Category.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing audio message {MessageId}", message.Id);
            
            await Clients.Caller.SendAsync("ProcessingError", new 
            { 
                messageId = message.Id, 
                error = ex.Message 
            });

            return new ProcessingResult
            {
                Success = false,
                Error = ex.Message,
                MessageId = message.Id
            };
        }
    }

    [HubMethodName("ProcessText")]
    public async Task<ProcessingResult> ProcessTextAsync(TextMessage message)
    {
        try
        {
            _metrics.RecordMessage("text", message.Text.Length);

            var session = await _sessionManager.GetSessionByConnectionIdAsync(Context.ConnectionId);
            if (session == null)
            {
                throw new InvalidOperationException("No active session found");
            }

            // Update session activity
            await _sessionManager.UpdateSessionActivityAsync(session.Id);

            // Send acknowledgment
            await Clients.Caller.SendAsync("ProcessingStarted", new { messageId = message.Id });

            // Route to intent classification
            var routingResult = await _serviceRouter.SendToRouterAsync(message.Text, session);

            // Queue for processing
            await _queueDispatcher.DispatchAsync(new QueueMessage
            {
                SessionId = session.Id,
                MessageId = message.Id,
                Type = routingResult.Intent.Category.ToString(),
                Payload = new
                {
                    Text = message.Text,
                    Intent = routingResult,
                    Context = session.Context
                },
                Priority = routingResult.Priority,
                Timestamp = DateTime.UtcNow
            });

            return new ProcessingResult
            {
                Success = true,
                MessageId = message.Id,
                Intent = routingResult.Intent.Category.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing text message {MessageId}", message.Id);
            
            await Clients.Caller.SendAsync("ProcessingError", new 
            { 
                messageId = message.Id, 
                error = ex.Message 
            });

            return new ProcessingResult
            {
                Success = false,
                Error = ex.Message,
                MessageId = message.Id
            };
        }
    }

    [HubMethodName("UpdateContext")]
    public async Task UpdateContextAsync(Dictionary<string, object> context)
    {
        try
        {
            var session = await _sessionManager.GetSessionByConnectionIdAsync(Context.ConnectionId);
            if (session == null)
            {
                throw new InvalidOperationException("No active session found");
            }

            await _sessionManager.UpdateSessionContextAsync(session.Id, context);
            await Clients.Caller.SendAsync("ContextUpdated", context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating context");
            throw;
        }
    }

    [HubMethodName("GetSessionInfo")]
    public async Task<UserSession?> GetSessionInfoAsync()
    {
        try
        {
            return await _sessionManager.GetSessionByConnectionIdAsync(Context.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting session info");
            throw;
        }
    }

    [HubMethodName("SendFeedback")]
    public async Task SendFeedbackAsync(FeedbackMessage feedback)
    {
        try
        {
            var session = await _sessionManager.GetSessionByConnectionIdAsync(Context.ConnectionId);
            if (session == null)
            {
                throw new InvalidOperationException("No active session found");
            }

            // Queue feedback for processing
            await _queueDispatcher.DispatchAsync(new QueueMessage
            {
                SessionId = session.Id,
                MessageId = feedback.Id,
                Type = "feedback",
                Payload = feedback,
                Priority = MessagePriority.Low,
                Timestamp = DateTime.UtcNow
            });

            await Clients.Caller.SendAsync("FeedbackReceived", new { feedbackId = feedback.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending feedback");
            throw;
        }
    }
    
    [HubMethodName("JoinSession")]
    public async Task<bool> JoinSessionAsync(string sessionId)
    {
        try
        {
            _logger.LogInformation("Client {ConnectionId} joining session {SessionId}", 
                Context.ConnectionId, sessionId);
                
            // Check if session exists
            var session = await _sessionManager.GetSessionAsync(sessionId);
            if (session == null)
            {
                // Create new session with the provided session ID
                var userId = Context.UserIdentifier ?? Context.ConnectionId;
                session = await _sessionManager.CreateSessionAsync(userId, Context.ConnectionId, sessionId);
                _logger.LogInformation("Created new session {SessionId} for connection {ConnectionId}", 
                    sessionId, Context.ConnectionId);
            }
            else
            {
                // Update existing session with new connection
                await _sessionManager.UpdateSessionConnectionAsync(sessionId, Context.ConnectionId);
                _logger.LogInformation("Updated existing session {SessionId} with connection {ConnectionId}", 
                    sessionId, Context.ConnectionId);
            }
            
            // Add to session group for audio responses
            await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
            await Groups.AddToGroupAsync(Context.ConnectionId, $"session:{sessionId}");
            
            // Send session info to client
            await Clients.Caller.SendAsync("SessionStarted", session);
            await Clients.Caller.SendAsync("JoinedSession", new { 
                sessionId, 
                success = true,
                session = session
            });
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining session {SessionId}", sessionId);
            await Clients.Caller.SendAsync("JoinedSession", new { sessionId, success = false, error = ex.Message });
            return false;
        }
    }
}

public class AudioMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public byte[] AudioData { get; set; } = Array.Empty<byte>();
    public string Format { get; set; } = "wav";
    public int SampleRate { get; set; } = 16000;
    public string? Language { get; set; }
}

public class VoiceRequestMessage
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
    public string AudioData { get; set; } = string.Empty; // Base64 encoded audio
    public string MimeType { get; set; } = "audio/wav";
    public string Language { get; set; } = "en-US";
    public string Timestamp { get; set; } = DateTime.UtcNow.ToString("O");
}

public class TextMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Text { get; set; } = string.Empty;
    public string? Language { get; set; }
}

public class ProcessingResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string MessageId { get; set; } = string.Empty;
    public string? TranscriptionId { get; set; }
    public string? Intent { get; set; }
}

public class FeedbackMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string MessageId { get; set; } = string.Empty;
    public FeedbackType Type { get; set; }
    public int? Rating { get; set; }
    public string? Comment { get; set; }
}

public enum FeedbackType
{
    Positive,
    Negative,
    Correction,
    Suggestion
}