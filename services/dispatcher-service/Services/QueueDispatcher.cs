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
            var queueName = GetQueueNameForMessageType(message.Type);
            
            _logger.LogInformation("Preparing to dispatch message - MessageId: {MessageId}, Type: {Type}, Queue: {Queue}, SessionId: {SessionId}",
                message.MessageId, message.Type, queueName, message.SessionId);
            
            var messageJson = JsonSerializer.Serialize(message);
            _logger.LogDebug("Message content - MessageId: {MessageId}, Content: {Content}",
                message.MessageId, messageJson);
            
            var serviceBusMessage = new ServiceBusMessage(messageJson)
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
            
            _logger.LogInformation("Successfully dispatched message - MessageId: {MessageId}, Type: {Type}, Queue: {Queue}, Priority: {Priority}", 
                message.MessageId, message.Type, queueName, message.Priority);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch message - MessageId: {MessageId}, Type: {Type}", 
                message.MessageId, message.Type);
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
            var messageId = Guid.NewGuid().ToString();
            
            // Log the incoming TTS request details
            _logger.LogInformation("TTS Request Initiated - SessionId: {SessionId}, MessageId: {MessageId}, Timestamp: {Timestamp}",
                sessionId, messageId, DateTime.UtcNow);
            
            // Log the payload content for debugging
            var payloadJson = JsonSerializer.Serialize(payload);
            _logger.LogDebug("TTS Request Payload - MessageId: {MessageId}, Payload: {Payload}",
                messageId, payloadJson);
            
            var message = new QueueMessage
            {
                SessionId = sessionId,
                MessageId = messageId,
                Type = "synthesize",
                Payload = payload,
                Priority = MessagePriority.High,
                Timestamp = DateTime.UtcNow
            };

            _logger.LogInformation("Dispatching TTS message to queue - MessageId: {MessageId}, Queue: tts-processing",
                messageId);
            
            await DispatchAsync(message);
            
            _logger.LogInformation("TTS Request Successfully Queued - MessageId: {MessageId}, SessionId: {SessionId}",
                messageId, sessionId);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending to TTS queue - SessionId: {SessionId}", sessionId);
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
    
    private string GetQueueNameForMessageType(string messageType)
    {
        return messageType.ToLower() switch
        {
            "claude-request" or "coding" or "explanation" or "debugging" => "claude-processing",
            "generate" or "refactor" => "code-generation",
            "synthesize" or "voice-response" => "tts-processing",
            _ => "dispatcher"
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