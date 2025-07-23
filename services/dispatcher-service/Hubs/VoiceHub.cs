using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Identity.Web.Resource;
using VoiceCode.Common.Models;
using VoiceCode.Common.Enums;
using VoiceCode.DispatcherService.Services;

namespace VoiceCode.DispatcherService.Hubs;

[Authorize]
[RequiredScope(RequiredScopesConfigurationKey = "AzureAd:Scopes")]
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
            var userId = Context.UserIdentifier ?? Context.ConnectionId;
            var session = await _sessionManager.CreateSessionAsync(userId, Context.ConnectionId);
            
            await Groups.AddToGroupAsync(Context.ConnectionId, session.Id);
            await Clients.Caller.SendAsync("SessionStarted", session);

            _logger.LogInformation("Client connected: {ConnectionId}, Session: {SessionId}", 
                Context.ConnectionId, session.Id);
            
            _metrics.RecordConnection();
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
}

public class AudioMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public byte[] AudioData { get; set; } = Array.Empty<byte>();
    public string Format { get; set; } = "wav";
    public int SampleRate { get; set; } = 16000;
    public string? Language { get; set; }
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