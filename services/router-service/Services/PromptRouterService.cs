using Microsoft.Extensions.Options;
using VoiceCode.Common.DTOs;
using VoiceCode.Common.Interfaces;
using VoiceCode.Common.Models;
using VoiceCode.Common.Enums;
using VoiceCode.RouterService.Configuration;
using DTOs = VoiceCode.Common.DTOs;

namespace VoiceCode.RouterService.Services;

public class PromptRouterService : IPromptRouter
{
    private readonly IIntentClassifier _intentClassifier;
    private readonly IContextManager _contextManager;
    private readonly IPromptEnhancer _promptEnhancer;
    private readonly IQueueService _queueService;
    private readonly RouterOptions _options;
    private readonly ILogger<PromptRouterService> _logger;

    public PromptRouterService(
        IIntentClassifier intentClassifier,
        IContextManager contextManager,
        IPromptEnhancer promptEnhancer,
        IQueueService queueService,
        IOptions<RouterOptions> options,
        ILogger<PromptRouterService> logger)
    {
        _intentClassifier = intentClassifier;
        _contextManager = contextManager;
        _promptEnhancer = promptEnhancer;
        _queueService = queueService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RoutingResult> RouteRequestAsync(RoutingRequest request)
    {
        using var activity = System.Diagnostics.Activity.Current;
        activity?.SetTag("routing.user_id", request.UserId);
        activity?.SetTag("routing.session_id", request.SessionId);

        try
        {
            _logger.LogInformation("Processing routing request for user {UserId}, session {SessionId}",
                request.UserId, request.SessionId);

            // Get user context
            var context = await _contextManager.GetContextAsync(request.UserId, request.SessionId);
            
            // Classify intent
            var intent = await _intentClassifier.ClassifyIntentAsync(request.Transcript, context);
            
            // Update context with new interaction
            await _contextManager.UpdateContextAsync(context, request.Transcript, intent);
            
            // Determine route based on intent
            var route = DetermineRoute(intent, context);
            
            // Enhance prompt if needed
            var enhancedPrompt = await _promptEnhancer.EnhancePromptAsync(
                request.Transcript, 
                intent, 
                context);

            // Send to appropriate queue
            var messageId = await _queueService.SendMessageAsync(route, new QueueMessage
            {
                RequestId = Guid.NewGuid().ToString(),
                UserId = request.UserId,
                SessionId = request.SessionId,
                Transcript = request.Transcript,
                Intent = intent,
                EnhancedPrompt = enhancedPrompt,
                Context = context,
                Timestamp = DateTime.UtcNow
            });

            var result = new RoutingResult
            {
                Route = route,
                Intent = intent,
                MessageId = messageId,
                EnhancedPrompt = enhancedPrompt
            };

            _logger.LogInformation("Routed request to {QueueName} with intent {IntentType}",
                route.QueueName, intent.Type);

            activity?.SetTag("routing.intent", intent.Type);
            activity?.SetTag("routing.queue", route.QueueName);
            activity?.SetTag("routing.confidence", intent.Confidence);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to route request");
            activity?.SetTag("routing.error", ex.Message);
            throw;
        }
    }

    public async Task<Intent> ClassifyIntentAsync(string transcript)
    {
        var context = new UserContext 
        { 
            UserId = "anonymous", 
            SessionId = Guid.NewGuid().ToString() 
        };
        
        return await _intentClassifier.ClassifyIntentAsync(transcript, context);
    }

    public async Task<UserContext> GetContextAsync(string userId, string sessionId)
    {
        return await _contextManager.GetContextAsync(userId, sessionId);
    }

    private DTOs.Route DetermineRoute(Intent intent, UserContext context)
    {
        // Check if we have a specific route configuration for this intent
        if (_options.Routes.TryGetValue(intent.Type, out var routeConfig))
        {
            return new DTOs.Route
            {
                QueueName = routeConfig.QueueName,
                Subject = routeConfig.Subject
            };
        }

        // Default routing based on intent category
        return intent.Category switch
        {
            IntentCategory.CodeGeneration => new DTOs.Route 
            { 
                QueueName = "code-generation", 
                Subject = "generate" 
            },
            IntentCategory.CodeExplanation => new DTOs.Route 
            { 
                QueueName = "code-generation", 
                Subject = "explain" 
            },
            IntentCategory.CodeRefactoring => new DTOs.Route 
            { 
                QueueName = "code-generation", 
                Subject = "refactor" 
            },
            IntentCategory.ErrorFixing => new DTOs.Route 
            { 
                QueueName = "code-generation", 
                Subject = "fix" 
            },
            IntentCategory.Testing => new DTOs.Route 
            { 
                QueueName = "code-generation", 
                Subject = "test" 
            },
            IntentCategory.Documentation => new DTOs.Route 
            { 
                QueueName = "code-generation", 
                Subject = "document" 
            },
            IntentCategory.ProjectManagement => new DTOs.Route 
            { 
                QueueName = "project-management", 
                Subject = "manage" 
            },
            IntentCategory.SystemCommand => new DTOs.Route 
            { 
                QueueName = "system-commands", 
                Subject = "command" 
            },
            _ => new DTOs.Route 
            { 
                QueueName = "general", 
                Subject = "process" 
            }
        };
    }
}

public class QueueMessage
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