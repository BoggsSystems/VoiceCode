using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using VoiceCode.ObserverService.Configuration;
using VoiceCode.ObserverService.Models;

namespace VoiceCode.ObserverService.Services;

public class ServiceBusPublisher : IServiceBusPublisher
{
    private readonly ILogger<ServiceBusPublisher> _logger;
    private readonly ServiceBusClient _serviceBusClient;
    private readonly ServiceBusSender _narrationSender;
    private readonly ServiceBusOptions _options;

    public ServiceBusPublisher(
        ILogger<ServiceBusPublisher> logger,
        ServiceBusClient serviceBusClient,
        IOptions<ServiceBusOptions> options)
    {
        _logger = logger;
        _serviceBusClient = serviceBusClient;
        _options = options.Value;
        _narrationSender = _serviceBusClient.CreateSender(_options.NarrationQueueName);
    }

    public async Task PublishNarrationAsync(NarrationRequest narration, CancellationToken cancellationToken = default)
    {
        try
        {
            // Convert to TTS request format
            var ttsRequest = new
            {
                id = Guid.NewGuid().ToString(),
                text = narration.Text,
                sessionId = narration.SessionId,
                metadata = new
                {
                    taskId = $"narration-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                    source = "observer",
                    workerId = narration.WorkerId,
                    priority = narration.Priority.ToString()
                }
            };
            
            var messageBody = JsonConvert.SerializeObject(ttsRequest);
            var message = new ServiceBusMessage(messageBody)
            {
                ContentType = "application/json",
                SessionId = narration.SessionId,
                Subject = "NarrationRequest",
                ApplicationProperties =
                {
                    ["Priority"] = narration.Priority.ToString(),
                    ["WorkerId"] = narration.WorkerId
                }
            };

            // Set message priority
            if (narration.Priority == NarrationPriority.Critical)
            {
                message.TimeToLive = TimeSpan.FromMinutes(1); // Short TTL for critical messages
            }

            await _narrationSender.SendMessageAsync(message, cancellationToken);

            _logger.LogInformation("Published narration for session {SessionId} with priority {Priority}", 
                narration.SessionId, narration.Priority);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing narration for session {SessionId}", narration.SessionId);
            throw;
        }
    }
}