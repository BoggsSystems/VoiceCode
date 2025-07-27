using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.SignalR;
using VoiceCode.Common.Models;
using VoiceCode.DispatcherService.Hubs;

namespace VoiceCode.DispatcherService.Services;

public class AudioResponseProcessor : BackgroundService
{
    private readonly ILogger<AudioResponseProcessor> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHubContext<VoiceHub> _voiceHub;
    private ServiceBusClient? _serviceBusClient;
    private ServiceBusProcessor? _processor;

    public AudioResponseProcessor(
        ILogger<AudioResponseProcessor> logger,
        IConfiguration configuration,
        IHubContext<VoiceHub> voiceHub)
    {
        _logger = logger;
        _configuration = configuration;
        _voiceHub = voiceHub;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var connectionString = _configuration.GetConnectionString("ServiceBus");
            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogWarning("Service Bus connection string not found. Audio response processing disabled.");
                return;
            }

            _logger.LogInformation("Initializing audio response processor...");
            _serviceBusClient = new ServiceBusClient(connectionString);
            
            _processor = _serviceBusClient.CreateProcessor("audio-responses", new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = 10,
                AutoCompleteMessages = false
            });

            _processor.ProcessMessageAsync += ProcessAudioResponseAsync;
            _processor.ProcessErrorAsync += ProcessErrorAsync;

            await _processor.StartProcessingAsync(stoppingToken);
            _logger.LogInformation("Audio response processor started, listening to audio-responses queue");

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start audio response processor");
            throw;
        }
    }

    private async Task ProcessAudioResponseAsync(ProcessMessageEventArgs args)
    {
        try
        {
            var body = Encoding.UTF8.GetString(args.Message.Body);
            _logger.LogInformation("Processing audio response from queue");

            var audioResponse = JsonSerializer.Deserialize<AudioResponseRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (audioResponse == null || string.IsNullOrEmpty(audioResponse.SessionId))
            {
                _logger.LogWarning("Invalid audio response message - missing session ID");
                await args.CompleteMessageAsync(args.Message);
                return;
            }

            _logger.LogInformation("Broadcasting audio response for session {SessionId}, task {TaskId}", 
                audioResponse.SessionId, audioResponse.TaskId);

            // Create response message for SignalR
            var responseMessage = new AudioResponseMessage
            {
                SessionId = audioResponse.SessionId,
                TaskId = audioResponse.TaskId,
                AudioUrl = audioResponse.AudioBlobPath,
                TranscriptionText = audioResponse.TranscriptionText,
                DurationSeconds = audioResponse.DurationSeconds,
                Timestamp = DateTime.UtcNow
            };

            // Broadcast to the specific session via SignalR
            await _voiceHub.Clients.Group(audioResponse.SessionId)
                .SendAsync("AudioResponseReady", responseMessage, CancellationToken.None);
            
            // Also broadcast to a user-specific group if available
            await _voiceHub.Clients.Group($"session:{audioResponse.SessionId}")
                .SendAsync("AudioResponseReady", responseMessage, CancellationToken.None);

            _logger.LogInformation("Successfully broadcast audio response for session {SessionId}", 
                audioResponse.SessionId);

            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing audio response");
            await args.AbandonMessageAsync(args.Message);
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Error in Service Bus processor");
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor != null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }

        if (_serviceBusClient != null)
        {
            await _serviceBusClient.DisposeAsync();
        }
        
        await base.StopAsync(cancellationToken);
    }
}