using Azure.Messaging.ServiceBus;
using System.Text.Json;
using VoiceCode.Common.DTOs;

namespace VoiceCode.RouterService.Services;

public interface IQueueService
{
    Task<string> SendMessageAsync(Route route, object message);
    Task<bool> SendBatchAsync(Route route, List<object> messages);
}

public class ServiceBusQueueService : IQueueService
{
    private readonly ServiceBusClient _serviceBusClient;
    private readonly ILogger<ServiceBusQueueService> _logger;
    private readonly Dictionary<string, ServiceBusSender> _senders;
    private readonly JsonSerializerOptions _jsonOptions;

    public ServiceBusQueueService(
        ServiceBusClient serviceBusClient,
        ILogger<ServiceBusQueueService> logger)
    {
        _serviceBusClient = serviceBusClient;
        _logger = logger;
        _senders = new Dictionary<string, ServiceBusSender>();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    public async Task<string> SendMessageAsync(Route route, object message)
    {
        try
        {
            var sender = GetOrCreateSender(route.QueueName);
            var messageId = Guid.NewGuid().ToString();

            var serviceBusMessage = new ServiceBusMessage
            {
                Body = BinaryData.FromString(JsonSerializer.Serialize(message, _jsonOptions)),
                MessageId = messageId,
                Subject = route.Subject,
                ContentType = "application/json",
                TimeToLive = TimeSpan.FromMinutes(5)
            };

            // Add message properties
            serviceBusMessage.ApplicationProperties["MessageType"] = message.GetType().Name;
            serviceBusMessage.ApplicationProperties["Timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            await sender.SendMessageAsync(serviceBusMessage);

            _logger.LogInformation("Sent message {MessageId} to queue {QueueName} with subject {Subject}",
                messageId, route.QueueName, route.Subject);

            return messageId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message to queue {QueueName}", route.QueueName);
            throw;
        }
    }

    public async Task<bool> SendBatchAsync(Route route, List<object> messages)
    {
        if (!messages.Any())
            return true;

        try
        {
            var sender = GetOrCreateSender(route.QueueName);
            var batch = await sender.CreateMessageBatchAsync();

            foreach (var message in messages)
            {
                var serviceBusMessage = new ServiceBusMessage
                {
                    Body = BinaryData.FromString(JsonSerializer.Serialize(message, _jsonOptions)),
                    MessageId = Guid.NewGuid().ToString(),
                    Subject = route.Subject,
                    ContentType = "application/json",
                    TimeToLive = TimeSpan.FromMinutes(5)
                };

                if (!batch.TryAddMessage(serviceBusMessage))
                {
                    // Send the current batch and create a new one
                    await sender.SendMessagesAsync(batch);
                    batch = await sender.CreateMessageBatchAsync();
                    
                    if (!batch.TryAddMessage(serviceBusMessage))
                    {
                        _logger.LogError("Message too large for batch");
                        return false;
                    }
                }
            }

            // Send any remaining messages
            if (batch.Count > 0)
            {
                await sender.SendMessagesAsync(batch);
            }

            _logger.LogInformation("Sent batch of {Count} messages to queue {QueueName}",
                messages.Count, route.QueueName);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send batch to queue {QueueName}", route.QueueName);
            return false;
        }
    }

    private ServiceBusSender GetOrCreateSender(string queueName)
    {
        if (!_senders.TryGetValue(queueName, out var sender))
        {
            sender = _serviceBusClient.CreateSender(queueName);
            _senders[queueName] = sender;
        }
        return sender;
    }

    public void Dispose()
    {
        foreach (var sender in _senders.Values)
        {
            sender.DisposeAsync().AsTask().Wait();
        }
        _senders.Clear();
    }
}