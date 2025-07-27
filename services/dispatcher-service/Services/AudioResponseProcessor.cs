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
            _logger.LogInformation("AudioResponseProcessor starting...");
            
            var connectionString = _configuration.GetConnectionString("ServiceBus") ?? 
                Environment.GetEnvironmentVariable("AZURE_SERVICE_BUS_CONNECTION_STRING");
            
            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogError("Service Bus connection string not found in configuration or environment. Audio response processing disabled.");
                _logger.LogError("Checked: ConnectionStrings:ServiceBus and AZURE_SERVICE_BUS_CONNECTION_STRING");
                return;
            }

            _logger.LogInformation("Initializing audio response processor with Service Bus connection...");
            _serviceBusClient = new ServiceBusClient(connectionString);
            
            _processor = _serviceBusClient.CreateProcessor("audio-responses", new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = 10,
                AutoCompleteMessages = false
            });

            _processor.ProcessMessageAsync += ProcessAudioResponseAsync;
            _processor.ProcessErrorAsync += ProcessErrorAsync;

            _logger.LogInformation("Starting audio response processor for queue: audio-responses");
            await _processor.StartProcessingAsync(stoppingToken);
            _logger.LogInformation("Audio response processor started successfully, listening to audio-responses queue");

            // Keep the service running
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                _logger.LogDebug("Audio response processor is running...");
            }
        }
        catch (TaskCanceledException)
        {
            _logger.LogInformation("Audio response processor cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start audio response processor: {Message}", ex.Message);
            throw;
        }
    }

    private async Task ProcessAudioResponseAsync(ProcessMessageEventArgs args)
    {
        try
        {
            var body = Encoding.UTF8.GetString(args.Message.Body);
            _logger.LogInformation("Processing audio response from queue, message size: {Size} bytes", body.Length);
            _logger.LogDebug("Audio response message body: {Body}", body);

            var audioResponse = JsonSerializer.Deserialize<AudioResponseRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (audioResponse == null || string.IsNullOrEmpty(audioResponse.SessionId))
            {
                _logger.LogWarning("Invalid audio response message - missing session ID. Body: {Body}", body);
                await args.CompleteMessageAsync(args.Message);
                return;
            }

            _logger.LogInformation("Broadcasting audio response for session {SessionId}, task {TaskId}, AudioUrl: {AudioUrl}, Text: {Text}", 
                audioResponse.SessionId, audioResponse.TaskId, audioResponse.AudioBlobPath, 
                audioResponse.TranscriptionText?.Substring(0, Math.Min(audioResponse.TranscriptionText.Length, 100)));

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

            _logger.LogInformation("Sending AudioResponseReady via SignalR with AudioUrl: {AudioUrl}, Duration: {Duration}s", 
                responseMessage.AudioUrl, responseMessage.DurationSeconds);
            _logger.LogDebug("Full SignalR message: {Message}", JsonSerializer.Serialize(responseMessage));

            // Broadcast to the specific session via SignalR
            await _voiceHub.Clients.Group(audioResponse.SessionId)
                .SendAsync("AudioResponseReady", responseMessage, CancellationToken.None);
            
            // Also broadcast to a user-specific group if available
            await _voiceHub.Clients.Group($"session:{audioResponse.SessionId}")
                .SendAsync("AudioResponseReady", responseMessage, CancellationToken.None);

            _logger.LogInformation("Successfully broadcast audio response for session {SessionId} to SignalR groups", 
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
        _logger.LogError(args.Exception, "Error in Service Bus processor for audio-responses queue. Source: {Source}, Namespace: {Namespace}", 
            args.ErrorSource, args.FullyQualifiedNamespace);
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