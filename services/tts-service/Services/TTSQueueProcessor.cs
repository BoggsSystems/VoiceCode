using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using VoiceCode.Common.Models;
using VoiceCode.Common.DTOs;
using VoiceCode.Common.Interfaces;
using VoiceCode.TTSService.Services.Interfaces;

namespace VoiceCode.TTSService.Services;

public class TTSQueueProcessor : BackgroundService
{
    private readonly ILogger<TTSQueueProcessor> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly IAudioResponseTableService _audioResponseTable;
    private ServiceBusClient? _serviceBusClient;
    private ServiceBusProcessor? _processor;

    public TTSQueueProcessor(
        ILogger<TTSQueueProcessor> logger,
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        IAudioResponseTableService audioResponseTable)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _audioResponseTable = audioResponseTable;
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
        var receivedAt = DateTime.UtcNow;
        var messageId = args.Message.MessageId;
        
        try
        {
            var body = Encoding.UTF8.GetString(args.Message.Body);
            
            _logger.LogInformation("TTS Queue Message Received - MessageId: {MessageId}, SessionId: {SessionId}, SequenceNumber: {SequenceNumber}, EnqueuedTime: {EnqueuedTime}",
                messageId, args.Message.SessionId, args.Message.SequenceNumber, args.Message.EnqueuedTime);
            
            // Log message properties
            if (args.Message.ApplicationProperties.Count > 0)
            {
                var properties = string.Join(", ", args.Message.ApplicationProperties.Select(p => $"{p.Key}={p.Value}"));
                _logger.LogDebug("Message Properties - MessageId: {MessageId}, Properties: {Properties}",
                    messageId, properties);
            }

            // Parse the JSON message
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            
            var requestId = root.TryGetProperty("id", out var idProp) ? idProp.GetString() : Guid.NewGuid().ToString();
            var text = root.TryGetProperty("text", out var textProp) ? textProp.GetString() : "";
            var sessionId = root.TryGetProperty("sessionId", out var sessionProp) ? sessionProp.GetString() : null;
            
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("TTS Request Empty Text - RequestId: {RequestId}, MessageId: {MessageId}, SessionId: {SessionId}",
                    requestId, messageId, sessionId ?? "none");
                await args.CompleteMessageAsync(args.Message);
                return;
            }

            // Extract metadata if present
            var metadata = root.TryGetProperty("metadata", out var metaProp) ? metaProp : default;
            var taskId = metadata.ValueKind != JsonValueKind.Undefined && metadata.TryGetProperty("taskId", out var taskProp) 
                ? taskProp.GetString() : null;
            var originalCommand = metadata.ValueKind != JsonValueKind.Undefined && metadata.TryGetProperty("originalCommand", out var cmdProp) 
                ? cmdProp.GetString() : null;

            _logger.LogInformation("TTS Request Details - RequestId: {RequestId}, TaskId: {TaskId}, SessionId: {SessionId}, TextLength: {TextLength}, Command: {Command}", 
                requestId, taskId ?? "none", sessionId ?? "none", text.Length, originalCommand ?? "none");
            
            // Log first 100 chars of text for debugging
            var textPreview = text.Length > 100 ? text.Substring(0, 100) + "..." : text;
            _logger.LogDebug("TTS Text Preview - RequestId: {RequestId}, Text: {Text}", requestId, textPreview);
            
            // Log the full message for deep debugging
            _logger.LogTrace("Full TTS request message - RequestId: {RequestId}, Body: {Message}", requestId, body);

            using var scope = _serviceProvider.CreateScope();
            var ttsService = scope.ServiceProvider.GetRequiredService<VoiceCode.TTSService.Services.Interfaces.ITTSService>();

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
                StoreAudio = true  // Enable storage to get URL
            };

            // Log synthesis start
            var synthesisStartTime = DateTime.UtcNow;
            _logger.LogInformation("Starting TTS Synthesis - RequestId: {RequestId}, Voice: {Voice}, Style: {Style}",
                requestId, synthesisRequest.VoiceName, synthesisRequest.Style);
            
            // Synthesize speech
            var result = await ttsService.SynthesizeAsync(synthesisRequest);
            
            var synthesisEndTime = DateTime.UtcNow;
            var synthesisDuration = (synthesisEndTime - synthesisStartTime).TotalMilliseconds;
            
            if (result.AudioData != null && result.AudioData.Length > 0)
            {
                _logger.LogInformation("TTS Synthesis Completed - RequestId: {RequestId}, AudioBytes: {Bytes}, Duration: {Duration}ms, SynthesisTime: {SynthesisTime}ms", 
                    requestId, result.AudioData.Length, result.Duration, synthesisDuration);
                
                // Send audio URL to Dispatcher for SignalR broadcast
                if (!string.IsNullOrEmpty(result.AudioUrl))
                {
                    // Use provided session ID or generate one for tracking
                    var effectiveSessionId = !string.IsNullOrEmpty(sessionId) ? sessionId : $"auto-{requestId}";
                    
                    _logger.LogInformation("Preparing Audio Response - RequestId: {RequestId}, SessionId: {SessionId}, AudioUrl: {AudioUrl}",
                        requestId, effectiveSessionId, result.AudioUrl);
                    
                    await SendAudioResponseToDispatcher(effectiveSessionId, taskId ?? requestId, result.AudioUrl, text, result.Duration / 1000.0);
                    
                    _logger.LogInformation("Audio Response Sent - RequestId: {RequestId}, SessionId: {SessionId}",
                        requestId, effectiveSessionId);
                }
                else
                {
                    _logger.LogWarning("TTS No Audio URL - RequestId: {RequestId}, SessionId: {SessionId}", 
                        requestId, sessionId ?? "none");
                }
                
                var totalProcessingTime = (DateTime.UtcNow - receivedAt).TotalMilliseconds;
                _logger.LogInformation("TTS Request Completed - RequestId: {RequestId}, SessionId: {SessionId}, TotalTime: {TotalTime}ms, AudioUrl: {AudioUrl}, AudioDuration: {Duration}ms", 
                    sessionId ?? "none", requestId, totalProcessingTime, result.AudioUrl ?? "none", result.Duration);
            }
            else
            {
                _logger.LogError("TTS Synthesis Failed - RequestId: {RequestId}, SessionId: {SessionId}, Error: {Error}", 
                    requestId, sessionId ?? "none", result.Error ?? "Unknown error");
            }

            await args.CompleteMessageAsync(args.Message);
            
            var completionTime = (DateTime.UtcNow - receivedAt).TotalMilliseconds;
            _logger.LogInformation("TTS Message Completed - MessageId: {MessageId}, RequestId: {RequestId}, TotalProcessingTime: {Time}ms",
                messageId, requestId, completionTime);
        }
        catch (Exception ex)
        {
            var processingTime = (DateTime.UtcNow - receivedAt).TotalMilliseconds;
            _logger.LogError(ex, "TTS Request Failed - MessageId: {MessageId}, SessionId: {SessionId}, ProcessingTime: {Time}ms",
                messageId, args.Message.SessionId, processingTime);
            await args.AbandonMessageAsync(args.Message);
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Error in Service Bus processor");
        return Task.CompletedTask;
    }
    
    private async Task SendAudioResponseToDispatcher(string sessionId, string taskId, string audioUrl, string text, double durationSeconds)
    {
        try
        {
            var sender = _serviceBusClient!.CreateSender("audio-responses");
            
            var message = new AudioResponseRequest
            {
                SessionId = sessionId,
                TaskId = taskId,
                AudioBlobPath = audioUrl,
                TranscriptionText = text,
                DurationSeconds = durationSeconds
            };
            
            var messageBody = JsonSerializer.Serialize(message);
            var serviceBusMessage = new ServiceBusMessage(Encoding.UTF8.GetBytes(messageBody))
            {
                ContentType = "application/json",
                Subject = "audio-response",
                MessageId = Guid.NewGuid().ToString(),
                SessionId = sessionId
            };
            
            await sender.SendMessageAsync(serviceBusMessage);
            await sender.DisposeAsync();
            
            // Also store in Azure Table for HTTP polling
            await _audioResponseTable.StoreAudioResponseAsync(taskId, sessionId, audioUrl, text, durationSeconds);
            
            _logger.LogInformation("Audio Response Dispatched - SessionId: {SessionId}, TaskId: {TaskId}, AudioUrl: {AudioUrl}, Duration: {Duration}s, MessageId: {MessageId}", 
                sessionId, taskId, audioUrl, durationSeconds, serviceBusMessage.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send audio response to dispatcher");
        }
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