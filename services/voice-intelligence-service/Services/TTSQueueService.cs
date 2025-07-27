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
        var sendStartTime = DateTime.UtcNow;
        var requestId = Guid.NewGuid().ToString();
        
        _logger.LogInformation("Preparing TTS Request - RequestId: {RequestId}, TaskId: {TaskId}, SessionId: {SessionId}, TextLength: {TextLength}",
            requestId, voiceResponse.TaskId, voiceResponse.SessionId, voiceResponse.SpokenResponse?.Length ?? 0);
        
        try
        {
            EnsureInitialized();
            
            // Log the spoken response content for debugging
            var textPreview = voiceResponse.SpokenResponse?.Length > 100 
                ? voiceResponse.SpokenResponse.Substring(0, 100) + "..." 
                : voiceResponse.SpokenResponse;
            _logger.LogDebug("TTS Text Preview - RequestId: {RequestId}, Text: {Text}",
                requestId, textPreview);
            
            // Format for TTS service
            var ttsRequest = new
            {
                id = requestId,
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
            _logger.LogDebug("TTS Request JSON - RequestId: {RequestId}, Body: {Body}",
                requestId, messageBody);
            
            var messageId = Guid.NewGuid().ToString();
            var message = new ServiceBusMessage(Encoding.UTF8.GetBytes(messageBody))
            {
                MessageId = messageId,
                ContentType = "application/json",
                Subject = "voice-synthesis",
                SessionId = voiceResponse.SessionId
            };
            
            // Add metadata to message properties
            message.ApplicationProperties["TaskId"] = voiceResponse.TaskId;
            message.ApplicationProperties["RequestId"] = requestId;
            message.ApplicationProperties["WorkerCount"] = voiceResponse.WorkersInvolved?.Count ?? 0;
            
            _logger.LogInformation("Sending TTS Request to Queue - RequestId: {RequestId}, MessageId: {MessageId}, Queue: tts-requests",
                requestId, messageId);

            await _ttsSender!.SendMessageAsync(message);
            
            var sendDuration = (DateTime.UtcNow - sendStartTime).TotalMilliseconds;
            _logger.LogInformation("TTS Request Queued Successfully - RequestId: {RequestId}, TaskId: {TaskId}, SessionId: {SessionId}, Workers: {Workers}, SendTime: {Time}ms", 
                requestId, voiceResponse.TaskId, voiceResponse.SessionId, 
                string.Join(", ", voiceResponse.WorkersInvolved ?? new List<string>()), sendDuration);
        }
        catch (Exception ex)
        {
            var failDuration = (DateTime.UtcNow - sendStartTime).TotalMilliseconds;
            _logger.LogError(ex, "Failed to send TTS request - RequestId: {RequestId}, TaskId: {TaskId}, SessionId: {SessionId}, FailTime: {Time}ms",
                requestId, voiceResponse.TaskId, voiceResponse.SessionId, failDuration);
            throw;
        }
    }
}