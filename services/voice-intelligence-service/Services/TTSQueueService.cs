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
    private readonly IConfiguration _configuration;
    private ServiceBusClient? _serviceBusClient;
    private ServiceBusSender? _ttsSender;

    public TTSQueueService(ILogger<TTSQueueService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }
    
    private void EnsureInitialized()
    {
        if (_serviceBusClient == null)
        {
            var connectionString = _configuration["ServiceBus:ConnectionString"] ?? 
                Environment.GetEnvironmentVariable("AZURE_SERVICE_BUS_CONNECTION_STRING");
            
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("Service Bus connection string not configured");
            }
            
            _serviceBusClient = new ServiceBusClient(connectionString);
            _ttsSender = _serviceBusClient.CreateSender("tts-requests");
        }
    }

    public async Task SendToTTSAsync(VoiceResponse voiceResponse)
    {
        try
        {
            EnsureInitialized();
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

            await _ttsSender!.SendMessageAsync(message);
            
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