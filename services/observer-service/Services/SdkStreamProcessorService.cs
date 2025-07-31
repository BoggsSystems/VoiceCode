using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using VoiceCode.ObserverService.Configuration;
using VoiceCode.ObserverService.Models;

namespace VoiceCode.ObserverService.Services;

public class SdkStreamProcessorService : BackgroundService
{
    private readonly ILogger<SdkStreamProcessorService> _logger;
    private readonly ServiceBusClient _serviceBusClient;
    private readonly IStreamProcessor _streamProcessor;
    private readonly ServiceBusOptions _options;
    private ServiceBusProcessor? _processor;

    public SdkStreamProcessorService(
        ILogger<SdkStreamProcessorService> logger,
        ServiceBusClient serviceBusClient,
        IStreamProcessor streamProcessor,
        IOptions<ServiceBusOptions> options)
    {
        _logger = logger;
        _serviceBusClient = serviceBusClient;
        _streamProcessor = streamProcessor;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = _serviceBusClient.CreateProcessor(
            _options.StreamQueueName,
            new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = _options.MaxConcurrentMessages,
                PrefetchCount = _options.PrefetchCount,
                AutoCompleteMessages = false
            });

        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);

        _logger.LogInformation("SDK Stream Processor Service started, listening to queue: {Queue}", _options.StreamQueueName);

        // Keep the service running
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        try
        {
            var body = args.Message.Body.ToString();
            var streamEvent = JsonConvert.DeserializeObject<SdkStreamEvent>(body);

            if (streamEvent == null)
            {
                _logger.LogWarning("Received null stream event");
                await args.CompleteMessageAsync(args.Message);
                return;
            }

            _logger.LogDebug("Processing SDK stream event: {EventType} for session {SessionId}", 
                streamEvent.EventType, streamEvent.SessionId);

            await _streamProcessor.ProcessStreamEventAsync(streamEvent);

            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing SDK stream message");
            
            // Abandon the message to retry later
            await args.AbandonMessageAsync(args.Message);
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, 
            "Error in SDK stream processor. Source: {Source}, Namespace: {Namespace}", 
            args.ErrorSource, args.FullyQualifiedNamespace);

        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SDK Stream Processor Service is stopping");

        if (_processor != null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}