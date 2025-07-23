using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;
using VoiceCode.Common.Models;
using VoiceCode.Common.DTOs;
using VoiceCode.Common.Interfaces;
using VoiceCode.DispatcherService.Hubs;
using System.Linq;

namespace VoiceCode.DispatcherService.Services;

public class QueueProcessorService : BackgroundService
{
    private readonly ILogger<QueueProcessorService> _logger;
    private readonly ServiceBusClient _serviceBusClient;
    private readonly IHubContext<VoiceHub> _hubContext;
    private readonly IServiceRouter _serviceRouter;
    private readonly ISessionManager _sessionManager;
    private readonly ServiceBusProcessor _processor;

    public QueueProcessorService(
        ILogger<QueueProcessorService> logger,
        ServiceBusClient serviceBusClient,
        IHubContext<VoiceHub> hubContext,
        IServiceRouter serviceRouter,
        ISessionManager sessionManager)
    {
        _logger = logger;
        _serviceBusClient = serviceBusClient;
        _hubContext = hubContext;
        _serviceRouter = serviceRouter;
        _sessionManager = sessionManager;

        _processor = _serviceBusClient.CreateProcessor("dispatcher", new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 10,
            AutoCompleteMessages = false,
            PrefetchCount = 20
        });

        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Queue Processor Service starting");

        await _processor.StartProcessingAsync(stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        try
        {
            var body = args.Message.Body.ToString();
            var sessionId = args.Message.SessionId;
            var messageType = args.Message.Subject;

            _logger.LogInformation("Processing message {MessageId} of type {Type} for session {SessionId}",
                args.Message.MessageId, messageType, sessionId);

            var session = await _sessionManager.GetSessionAsync(sessionId);
            if (session == null)
            {
                _logger.LogWarning("Session {SessionId} not found, abandoning message", sessionId);
                await args.AbandonMessageAsync(args.Message);
                return;
            }

            switch (messageType?.ToLower())
            {
                case "claude-response":
                    await HandleClaudeResponse(body, session);
                    break;

                case "generation-complete":
                    await HandleGenerationComplete(body, session);
                    break;

                case "synthesis-complete":
                    await HandleSynthesisComplete(body, session);
                    break;

                case "error":
                    await HandleError(body, session);
                    break;

                default:
                    _logger.LogWarning("Unknown message type: {Type}", messageType);
                    break;
            }

            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message {MessageId}", args.Message.MessageId);
            await args.AbandonMessageAsync(args.Message);
        }
    }

    private async Task HandleClaudeResponse(string body, UserSession session)
    {
        try
        {
            var response = JsonSerializer.Deserialize<ClaudeResponse>(body);
            if (response == null) return;

            // Send response to client
            await _hubContext.Clients.Group(session.Id).SendAsync("ClaudeResponse", response);

            // If response contains code, send to generator
            if (response.CodeBlocks?.Any() == true)
            {
                var generatorInput = new CodeGenerationInput
                {
                    CodeBlocks = response.CodeBlocks.Select(cb => new CodeBlock
                    {
                        Language = cb.Language,
                        Content = cb.Code,
                        FileName = cb.FileName ?? string.Empty
                    }).ToList(),
                    TargetDirectory = session.Context.CurrentProject?.Path ?? "src"
                };

                await _serviceRouter.SendToGeneratorAsync(generatorInput, session.Id);
            }

            // Send response to TTS for voice synthesis
            if (!string.IsNullOrEmpty(response.VoiceResponse?.Text))
            {
                var ttsRequest = new SynthesisRequest
                {
                    Text = response.VoiceResponse.Text,
                    VoiceName = session.Context.Preferences.VoiceSettings.Voice,
                    Emotion = response.VoiceResponse.Emotion,
                    StoreAudio = true,
                    ReturnAudioData = false
                };

                await _serviceRouter.SendToTTSAsync(ttsRequest, session.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Claude response");
            await NotifyError(session.Id, "Failed to process Claude response");
        }
    }

    private async Task HandleGenerationComplete(string body, UserSession session)
    {
        try
        {
            var result = JsonSerializer.Deserialize<GeneratedFiles>(body);
            if (result == null) return;

            // Send generated files to client
            await _hubContext.Clients.Group(session.Id).SendAsync("GenerationComplete", result);

            // Send success notification via TTS
            var successMessage = result.Files.Count == 1 
                ? $"Code generation complete. Generated {result.Files[0].FileName}."
                : $"Code generation complete. Generated {result.Files.Count} files.";

            var ttsRequest = new SynthesisRequest
            {
                Text = successMessage,
                VoiceName = session.Context.Preferences.VoiceSettings.Voice,
                StoreAudio = true,
                ReturnAudioData = false
            };

            await _serviceRouter.SendToTTSAsync(ttsRequest, session.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling generation complete");
            await NotifyError(session.Id, "Failed to process generated files");
        }
    }

    private async Task HandleSynthesisComplete(string body, UserSession session)
    {
        try
        {
            var result = JsonSerializer.Deserialize<SynthesisResult>(body);
            if (result == null) return;

            // Send audio URL to client
            await _hubContext.Clients.Group(session.Id).SendAsync("AudioReady", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling synthesis complete");
            await NotifyError(session.Id, "Failed to process synthesized audio");
        }
    }

    private async Task HandleError(string body, UserSession session)
    {
        try
        {
            var error = JsonSerializer.Deserialize<ErrorMessage>(body);
            if (error == null) return;

            await _hubContext.Clients.Group(session.Id).SendAsync("Error", error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling error message");
        }
    }

    private async Task NotifyError(string sessionId, string message)
    {
        await _hubContext.Clients.Group(sessionId).SendAsync("Error", new ErrorMessage
        {
            Message = message,
            Timestamp = DateTime.UtcNow
        });
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, 
            "Error processing message: {ErrorSource}", args.ErrorSource);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Queue Processor Service stopping");
        
        await _processor.StopProcessingAsync(cancellationToken);
        await _processor.DisposeAsync();
        
        await base.StopAsync(cancellationToken);
    }
}

public class ErrorMessage
{
    public string Message { get; set; } = string.Empty;
    public string? Code { get; set; }
    public DateTime Timestamp { get; set; }
}