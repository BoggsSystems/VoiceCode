using Azure.Messaging.ServiceBus;
using System.Text;
using System.Text.Json;

namespace VoiceCode.RouterService.Services;

public class AudioResponseListener : BackgroundService
{
    private readonly ServiceBusClient _serviceBusClient;
    private readonly IAudioResponseTracker _audioTracker;
    private readonly ILogger<AudioResponseListener> _logger;
    private ServiceBusProcessor? _processor;

    public AudioResponseListener(
        ServiceBusClient serviceBusClient,
        IAudioResponseTracker audioTracker,
        ILogger<AudioResponseListener> logger)
    {
        _serviceBusClient = serviceBusClient;
        _audioTracker = audioTracker;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = _serviceBusClient.CreateProcessor("audio-responses", new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 10,
            AutoCompleteMessages = false
        });

        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;

        _logger.LogInformation("Starting audio response listener for queue: audio-responses");
        await _processor.StartProcessingAsync(stoppingToken);
        _logger.LogInformation("Audio response listener started successfully");

        // Keep the service running
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        try
        {
            var body = Encoding.UTF8.GetString(args.Message.Body);
            _logger.LogInformation("Processing audio response from queue, message size: {Size} bytes", body.Length);

            var audioResponse = JsonSerializer.Deserialize<AudioResponseMessage>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (audioResponse != null && !string.IsNullOrEmpty(audioResponse.TaskId))
            {
                // Store the audio response for HTTP polling
                var responseData = new AudioResponseData
                {
                    TaskId = audioResponse.TaskId,
                    AudioUrl = audioResponse.AudioUrl,
                    Text = audioResponse.Text,
                    Duration = audioResponse.Duration,
                    Status = "ready",
                    Timestamp = DateTime.UtcNow
                };

                await _audioTracker.StoreAudioResponseAsync(audioResponse.TaskId, responseData);
                _logger.LogInformation("Stored audio response for task {TaskId} for HTTP polling", audioResponse.TaskId);
            }

            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing audio response message");
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
        await base.StopAsync(cancellationToken);
    }
}

// Message structure from Service Bus
public class AudioResponseMessage
{
    public string TaskId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string AudioUrl { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public double Duration { get; set; }
    public string Type { get; set; } = "AudioResponseReady";
}