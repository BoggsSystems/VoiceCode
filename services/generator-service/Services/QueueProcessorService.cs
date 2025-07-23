using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using System.Text.Json;
using VoiceCode.Common.DTOs;
using VoiceCode.Common.Interfaces;
using VoiceCode.Common.Models;
using VoiceCode.Common.Enums;
using VoiceCode.GeneratorService.Configuration;
using DTOs = VoiceCode.Common.DTOs;

namespace VoiceCode.GeneratorService.Services;

public class QueueProcessorService : BackgroundService
{
    private readonly ServiceBusClient _serviceBusClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly GeneratorOptions _options;
    private readonly ILogger<QueueProcessorService> _logger;
    private ServiceBusProcessor? _processor;

    public QueueProcessorService(
        ServiceBusClient serviceBusClient,
        IServiceProvider serviceProvider,
        IOptions<GeneratorOptions> options,
        ILogger<QueueProcessorService> logger)
    {
        _serviceBusClient = serviceBusClient;
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = _serviceBusClient.CreateProcessor("code-generation", new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 5,
            AutoCompleteMessages = false
        });

        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);

        _logger.LogInformation("Queue processor started");

        // Keep the service running
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var messageId = args.Message.MessageId;
        
        try
        {
            _logger.LogInformation("Processing message {MessageId} with subject {Subject}",
                messageId, args.Message.Subject);

            // Deserialize the message
            var messageBody = args.Message.Body.ToString();
            var codeGenRequest = JsonSerializer.Deserialize<CodeGenerationQueueMessage>(messageBody);

            if (codeGenRequest == null)
            {
                _logger.LogError("Failed to deserialize message {MessageId}", messageId);
                await args.DeadLetterMessageAsync(args.Message, "InvalidMessage");
                return;
            }

            // Create a scope for dependency injection
            using var scope = _serviceProvider.CreateScope();
            var codeGenerator = scope.ServiceProvider.GetRequiredService<ICodeGenerator>();
            var claudeService = scope.ServiceProvider.GetRequiredService<IClaudeService>();

            // Process based on subject/operation type
            var result = args.Message.Subject?.ToLower() switch
            {
                "generate" => await HandleGenerateCodeAsync(codeGenRequest, claudeService, codeGenerator),
                "explain" => await HandleExplainCodeAsync(codeGenRequest, claudeService),
                "fix" => await HandleFixCodeAsync(codeGenRequest, claudeService),
                "refactor" => await HandleRefactorCodeAsync(codeGenRequest, claudeService),
                _ => throw new NotSupportedException($"Operation {args.Message.Subject} not supported")
            };

            // Send result to dispatcher queue
            await SendResultAsync(result, codeGenRequest.RequestId);

            // Complete the message
            await args.CompleteMessageAsync(args.Message);

            _logger.LogInformation("Successfully processed message {MessageId}", messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message {MessageId}", messageId);

            // Retry or dead letter based on delivery count
            if (args.Message.DeliveryCount >= 3)
            {
                await args.DeadLetterMessageAsync(args.Message, ex.Message);
            }
            else
            {
                await args.AbandonMessageAsync(args.Message);
            }
        }
    }

    private async Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Error in message processor for {FullyQualifiedNamespace}/{EntityPath}",
            args.FullyQualifiedNamespace, args.EntityPath);
    }

    private async Task<ProcessingResult> HandleGenerateCodeAsync(
        CodeGenerationQueueMessage request,
        IClaudeService claudeService,
        ICodeGenerator codeGenerator)
    {
        // Call Claude to generate code
        var claudeRequest = new DTOs.CodeGenerationRequest
        {
            Type = "generate",
            Instructions = request.EnhancedPrompt ?? request.Transcript,
            Language = request.Context.SessionData.GetValueOrDefault("current_language")?.ToString() ?? "csharp",
            Context = new Dictionary<string, object>
            {
                { "user_context", request.Context },
                { "intent", request.Intent }
            }
        };

        var claudeResponse = await claudeService.GenerateCodeAsync(claudeRequest);

        // Process the generated code
        var codeBlocks = ParseCodeBlocks(claudeResponse);
        var generationInput = new CodeGenerationInput
        {
            CodeBlocks = codeBlocks,
            TargetDirectory = request.Context.SessionData.GetValueOrDefault("project_directory")?.ToString() ?? "generated"
        };

        var generatedFiles = await codeGenerator.GenerateFilesAsync(generationInput);

        return new ProcessingResult
        {
            RequestId = request.RequestId,
            UserId = request.UserId,
            SessionId = request.SessionId,
            Status = generatedFiles.Errors.Any() ? ProcessingStatus.PartialSuccess : ProcessingStatus.Success,
            GeneratedFiles = generatedFiles,
            VoiceResponse = claudeResponse.VoiceResponse,
            Metadata = new Dictionary<string, object>
            {
                { "files_generated", generatedFiles.Files.Count },
                { "errors", generatedFiles.Errors }
            }
        };
    }

    private async Task<ProcessingResult> HandleExplainCodeAsync(
        CodeGenerationQueueMessage request,
        IClaudeService claudeService)
    {
        var explanation = await claudeService.ExplainCodeAsync(
            request.Context.SessionData.GetValueOrDefault("current_code")?.ToString() ?? "",
            request.Context.SessionData.GetValueOrDefault("current_language")?.ToString() ?? "csharp");

        return new ProcessingResult
        {
            RequestId = request.RequestId,
            UserId = request.UserId,
            SessionId = request.SessionId,
            Status = ProcessingStatus.Success,
            Explanation = explanation,
            Metadata = new Dictionary<string, object>
            {
                { "operation", "explain" }
            }
        };
    }

    private async Task<ProcessingResult> HandleFixCodeAsync(
        CodeGenerationQueueMessage request,
        IClaudeService claudeService)
    {
        var fixedCode = await claudeService.FixCodeAsync(
            request.Context.SessionData.GetValueOrDefault("current_code")?.ToString() ?? "",
            request.Context.SessionData.GetValueOrDefault("error_message")?.ToString() ?? "",
            request.Context.SessionData.GetValueOrDefault("current_language")?.ToString() ?? "csharp");

        return new ProcessingResult
        {
            RequestId = request.RequestId,
            UserId = request.UserId,
            SessionId = request.SessionId,
            Status = ProcessingStatus.Success,
            FixedCode = fixedCode,
            Metadata = new Dictionary<string, object>
            {
                { "operation", "fix" }
            }
        };
    }

    private async Task<ProcessingResult> HandleRefactorCodeAsync(
        CodeGenerationQueueMessage request,
        IClaudeService claudeService)
    {
        var refactoredCode = await claudeService.RefactorCodeAsync(
            request.Context.SessionData.GetValueOrDefault("current_code")?.ToString() ?? "",
            request.Transcript,
            request.Context.SessionData.GetValueOrDefault("current_language")?.ToString() ?? "csharp");

        return new ProcessingResult
        {
            RequestId = request.RequestId,
            UserId = request.UserId,
            SessionId = request.SessionId,
            Status = ProcessingStatus.Success,
            RefactoredCode = refactoredCode,
            Metadata = new Dictionary<string, object>
            {
                { "operation", "refactor" }
            }
        };
    }

    private List<CodeBlock> ParseCodeBlocks(DTOs.CodeGenerationResponse response)
    {
        var blocks = new List<CodeBlock>();
        
        // If we have code in the response, create a code block
        if (!string.IsNullOrEmpty(response.Code))
        {
            blocks.Add(new CodeBlock
            {
                Content = response.Code,
                Language = response.Language,
                FileName = null // Will be determined by the generator
            });
        }

        return blocks;
    }

    private async Task SendResultAsync(ProcessingResult result, string requestId)
    {
        try
        {
            var sender = _serviceBusClient.CreateSender("dispatcher");
            
            var message = new ServiceBusMessage
            {
                Body = BinaryData.FromString(JsonSerializer.Serialize(result)),
                MessageId = Guid.NewGuid().ToString(),
                Subject = "code-generation-result",
                ContentType = "application/json",
                CorrelationId = requestId
            };

            await sender.SendMessageAsync(message);
            _logger.LogInformation("Sent result for request {RequestId} to dispatcher", requestId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send result for request {RequestId}", requestId);
            throw;
        }
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

public class CodeGenerationQueueMessage
{
    public string RequestId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Transcript { get; set; } = string.Empty;
    public Intent Intent { get; set; } = new();
    public string? EnhancedPrompt { get; set; }
    public UserContext Context { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

public class ProcessingResult
{
    public string RequestId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public ProcessingStatus Status { get; set; }
    public GeneratedFiles? GeneratedFiles { get; set; }
    public string? Explanation { get; set; }
    public string? FixedCode { get; set; }
    public string? RefactoredCode { get; set; }
    public VoiceResponse? VoiceResponse { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

