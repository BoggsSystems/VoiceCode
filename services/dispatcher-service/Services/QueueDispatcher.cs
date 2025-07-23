using Azure.Messaging.ServiceBus;
using System.Text.Json;
using VoiceCode.Common.Models;
using VoiceCode.Common.Enums;

namespace VoiceCode.DispatcherService.Services;

public interface IQueueDispatcher
{
    Task DispatchAsync(QueueMessage message);
    Task<bool> SendToClaudeAsync(string sessionId, object payload);
    Task<bool> SendToGeneratorAsync(string sessionId, object payload);
    Task<bool> SendToTTSAsync(string sessionId, object payload);
}

public class QueueDispatcher : IQueueDispatcher, IDisposable
{
    private readonly ILogger<QueueDispatcher> _logger;
    private readonly ServiceBusClient _serviceBusClient;
    private readonly ServiceBusSender _claudeSender;
    private readonly ServiceBusSender _generatorSender;
    private readonly ServiceBusSender _ttsSender;
    private readonly ServiceBusSender _dispatcherSender;

    public QueueDispatcher(
        ILogger<QueueDispatcher> logger,
        ServiceBusClient serviceBusClient)
    {
        _logger = logger;
        _serviceBusClient = serviceBusClient;
        
        _claudeSender = _serviceBusClient.CreateSender("claude-processing");
        _generatorSender = _serviceBusClient.CreateSender("code-generation");
        _ttsSender = _serviceBusClient.CreateSender("tts-processing");
        _dispatcherSender = _serviceBusClient.CreateSender("dispatcher");
    }

    public async Task DispatchAsync(QueueMessage message)
    {
        try
        {
            var sender = GetSenderForMessageType(message.Type);
            
            var serviceBusMessage = new ServiceBusMessage(JsonSerializer.Serialize(message))
            {
                SessionId = message.SessionId,
                MessageId = message.MessageId,
                Subject = message.Type,
                ContentType = "application/json",
                TimeToLive = TimeSpan.FromMinutes(5)
            };

            // Add custom properties
            serviceBusMessage.ApplicationProperties["Priority"] = message.Priority.ToString();
            serviceBusMessage.ApplicationProperties["Timestamp"] = message.Timestamp.ToString("O");

            await sender.SendMessageAsync(serviceBusMessage);
            
            _logger.LogInformation("Dispatched message {MessageId} of type {Type} to queue", 
                message.MessageId, message.Type);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dispatching message {MessageId}", message.MessageId);
            throw;
        }
    }

    public async Task<bool> SendToClaudeAsync(string sessionId, object payload)
    {
        try
        {
            var message = new QueueMessage
            {
                SessionId = sessionId,
                MessageId = Guid.NewGuid().ToString(),
                Type = "claude-request",
                Payload = payload,
                Priority = MessagePriority.Normal,
                Timestamp = DateTime.UtcNow
            };

            await DispatchAsync(message);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending to Claude queue");
            return false;
        }
    }

    public async Task<bool> SendToGeneratorAsync(string sessionId, object payload)
    {
        try
        {
            var message = new QueueMessage
            {
                SessionId = sessionId,
                MessageId = Guid.NewGuid().ToString(),
                Type = "generate",
                Payload = payload,
                Priority = MessagePriority.Normal,
                Timestamp = DateTime.UtcNow
            };

            await DispatchAsync(message);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending to Generator queue");
            return false;
        }
    }

    public async Task<bool> SendToTTSAsync(string sessionId, object payload)
    {
        try
        {
            var message = new QueueMessage
            {
                SessionId = sessionId,
                MessageId = Guid.NewGuid().ToString(),
                Type = "synthesize",
                Payload = payload,
                Priority = MessagePriority.High,
                Timestamp = DateTime.UtcNow
            };

            await DispatchAsync(message);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending to TTS queue");
            return false;
        }
    }

    private ServiceBusSender GetSenderForMessageType(string messageType)
    {
        return messageType.ToLower() switch
        {
            "claude-request" or "coding" or "explanation" or "debugging" => _claudeSender,
            "generate" or "refactor" => _generatorSender,
            "synthesize" or "voice-response" => _ttsSender,
            _ => _dispatcherSender
        };
    }

    public void Dispose()
    {
        _claudeSender?.DisposeAsync().AsTask().Wait();
        _generatorSender?.DisposeAsync().AsTask().Wait();
        _ttsSender?.DisposeAsync().AsTask().Wait();
        _dispatcherSender?.DisposeAsync().AsTask().Wait();
    }
}

public class QueueMessage
{
    public string SessionId { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public object? Payload { get; set; }
    public MessagePriority Priority { get; set; } = MessagePriority.Normal;
    public DateTime Timestamp { get; set; }
}