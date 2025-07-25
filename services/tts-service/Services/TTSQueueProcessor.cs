using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using VoiceCode.Common.Models;
using VoiceCode.Common.DTOs;
using VoiceCode.TTSService.Services.Interfaces;

namespace VoiceCode.TTSService.Services;

public class TTSQueueProcessor : BackgroundService
{
    private readonly ILogger<TTSQueueProcessor> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private ServiceBusClient? _serviceBusClient;
    private ServiceBusProcessor? _processor;

    public TTSQueueProcessor(
        ILogger<TTSQueueProcessor> logger,
        IServiceProvider serviceProvider,
        IConfiguration configuration)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var connectionString = _configuration["ConnectionStrings:ServiceBus"] ?? 
                Environment.GetEnvironmentVariable("AZURE_SERVICE_BUS_CONNECTION_STRING");
            
            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogWarning("Service Bus connection string not found. TTS queue processing disabled.");
                return;
            }
            
            _logger.LogInformation("Initializing TTS queue processor...");
            _serviceBusClient = new ServiceBusClient(connectionString);
            
            _processor = _serviceBusClient.CreateProcessor("tts-requests", new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = 5,
                AutoCompleteMessages = false
            });

            _processor.ProcessMessageAsync += ProcessTTSRequestAsync;
            _processor.ProcessErrorAsync += ProcessErrorAsync;

            await _processor.StartProcessingAsync(stoppingToken);
            _logger.LogInformation("TTS queue processor started, listening to tts-requests queue");

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start TTS queue processor");
            throw;
        }
    }

    private async Task ProcessTTSRequestAsync(ProcessMessageEventArgs args)
    {
        try
        {
            var body = Encoding.UTF8.GetString(args.Message.Body);
            _logger.LogInformation("Processing TTS request from queue");

            // Parse the JSON message
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            
            var requestId = root.TryGetProperty("id", out var idProp) ? idProp.GetString() : Guid.NewGuid().ToString();
            var text = root.TryGetProperty("text", out var textProp) ? textProp.GetString() : "";
            var sessionId = root.TryGetProperty("sessionId", out var sessionProp) ? sessionProp.GetString() : null;
            
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("Received TTS request with empty text");
                await args.CompleteMessageAsync(args.Message);
                return;
            }

            _logger.LogInformation("Processing TTS request {Id} for session {SessionId} with text length {Length}", 
                requestId, sessionId ?? "none", text.Length);

            using var scope = _serviceProvider.CreateScope();
            var ttsService = scope.ServiceProvider.GetRequiredService<ITTSService>();

            // Create synthesis request
            var synthesisRequest = new SynthesisRequest
            {
                Text = text,
                VoiceName = "en-US-JennyNeural",
                Language = "en-US",
                OutputFormat = "audio-16khz-128kbitrate-mono-mp3",
                SessionId = sessionId,
                Style = "friendly",
                StyleDegree = 1.2,
                ReturnAudioData = true,
                StoreAudio = false
            };

            // Synthesize speech
            var result = await ttsService.SynthesizeAsync(synthesisRequest);
            
            if (result.AudioData != null && result.AudioData.Length > 0)
            {
                _logger.LogInformation("Successfully synthesized speech for request {Id}, {Length} bytes", 
                    requestId, result.AudioData.Length);
                
                // In a production system, you would:
                // 1. Store the audio in blob storage
                // 2. Send a notification with the audio URL
                // 3. Or stream it directly to the client
                
                // For now, we'll just log success
                _logger.LogInformation("TTS audio ready for session {SessionId}, content type: {ContentType}, duration: {Duration}ms", 
                    sessionId, result.ContentType, result.Duration);
            }
            else
            {
                _logger.LogWarning("Failed to synthesize speech for request {Id}: {Error}", 
                    requestId, result.Error);
            }

            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing TTS request");
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