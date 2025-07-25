using System.Text;
using Azure.Messaging.ServiceBus;
using Newtonsoft.Json;
using VoiceCode.VoiceIntelligenceService.Models;

namespace VoiceCode.VoiceIntelligenceService.Services;

public interface ITTSQueueService
{
    Task SendToTTSAsync(VoiceResponse voiceResponse);
}

public class TTSQueueService : ITTSQueueService
{
    private readonly ILogger<TTSQueueService> _logger;
    private readonly ServiceBusClient _serviceBusClient;
    private readonly ServiceBusSender _ttsSender;

    public TTSQueueService(ILogger<TTSQueueService> logger, IConfiguration configuration)
    {
        _logger = logger;
        
        var connectionString = configuration["ServiceBus:ConnectionString"] ?? 
            Environment.GetEnvironmentVariable("AZURE_SERVICE_BUS_CONNECTION_STRING");
        
        _serviceBusClient = new ServiceBusClient(connectionString);
        _ttsSender = _serviceBusClient.CreateSender("tts-requests");
    }

    public async Task SendToTTSAsync(VoiceResponse voiceResponse)
    {
        try
        {
            // Format for TTS service
            var ttsRequest = new
            {
                id = Guid.NewGuid().ToString(),
                text = voiceResponse.SpokenResponse,
                sessionId = voiceResponse.SessionId,
                metadata = new
                {
                    taskId = voiceResponse.TaskId,
                    workersInvolved = voiceResponse.WorkersInvolved,
                    timestamp = voiceResponse.Timestamp,
                    originalCommand = voiceResponse.Context.GetValueOrDefault("originalCommand")
                }
            };

            var messageBody = JsonConvert.SerializeObject(ttsRequest);
            var message = new ServiceBusMessage(Encoding.UTF8.GetBytes(messageBody))
            {
                ContentType = "application/json",
                Subject = "voice-synthesis",
                SessionId = voiceResponse.SessionId
            };

            await _ttsSender.SendMessageAsync(message);
            
            _logger.LogInformation("Sent voice response to TTS queue for task {TaskId}: {Response}", 
                voiceResponse.TaskId, voiceResponse.SpokenResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send voice response to TTS queue");
            throw;
        }
    }
}