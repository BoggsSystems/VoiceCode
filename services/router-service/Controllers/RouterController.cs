using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;
using VoiceCode.Common.DTOs;
using VoiceCode.Common.Interfaces;
using VoiceCode.Common.Models;
using VoiceCode.RouterService.Services;

namespace VoiceCode.RouterService.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
[RequiredScope(RequiredScopesConfigurationKey = "AzureAd:Scopes")]
public class RouterController : ControllerBase
{
    private readonly IPromptRouter _router;
    private readonly IIntentClassifier _classifier;
    private readonly IContextManager _contextManager;
    private readonly ILogger<RouterController> _logger;

    public RouterController(
        IPromptRouter router,
        IIntentClassifier classifier,
        IContextManager contextManager,
        ILogger<RouterController> logger)
    {
        _router = router;
        _classifier = classifier;
        _contextManager = contextManager;
        _logger = logger;
    }

    [HttpPost("route")]
    public async Task<ActionResult<RoutingResult>> RouteRequest([FromBody] RoutingRequest request)
    {
        var requestId = Guid.NewGuid().ToString();
        using var activity = System.Diagnostics.Activity.Current;
        activity?.SetTag("request.id", requestId);
        activity?.SetTag("user.id", request.UserId);

        try
        {
            _logger.LogInformation("Processing routing request {RequestId} for user {UserId}",
                requestId, request.UserId);

            var result = await _router.RouteRequestAsync(request);
            
            _logger.LogInformation("Routed request {RequestId} to {Queue} with intent {Intent}",
                requestId, result.Route.QueueName, result.Intent.Type);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to route request {RequestId}", requestId);
            return StatusCode(500, new { error = "Routing failed", requestId });
        }
    }

    [HttpPost("classify")]
    public async Task<ActionResult<Intent>> ClassifyIntent([FromBody] ClassifyRequest request)
    {
        try
        {
            var context = new UserContext 
            { 
                UserId = User.Identity?.Name ?? "anonymous",
                SessionId = request.SessionId ?? Guid.NewGuid().ToString()
            };

            var intent = await _classifier.ClassifyIntentAsync(request.Text, context);
            
            _logger.LogInformation("Classified intent as {Type} with confidence {Confidence:P}",
                intent.Type, intent.Confidence);

            return Ok(intent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Intent classification failed");
            return StatusCode(500, new { error = "Classification failed" });
        }
    }

    [HttpPost("analyze")]
    public async Task<ActionResult<List<IntentAnalysis>>> AnalyzeText([FromBody] AnalyzeRequest request)
    {
        try
        {
            var analyses = await _classifier.AnalyzeIntentsAsync(
                request.Text, 
                request.Context ?? string.Empty);

            return Ok(analyses);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Text analysis failed");
            return StatusCode(500, new { error = "Analysis failed" });
        }
    }

    [HttpGet("context/{userId}/{sessionId}")]
    public async Task<ActionResult<UserContext>> GetContext(string userId, string sessionId)
    {
        try
        {
            var context = await _contextManager.GetContextAsync(userId, sessionId);
            return Ok(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get context for user {UserId}", userId);
            return StatusCode(500, new { error = "Failed to retrieve context" });
        }
    }

    [HttpDelete("context/{userId}/{sessionId}")]
    public async Task<ActionResult> ClearContext(string userId, string sessionId)
    {
        try
        {
            await _contextManager.ClearContextAsync(userId, sessionId);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear context for user {UserId}", userId);
            return StatusCode(500, new { error = "Failed to clear context" });
        }
    }

    [HttpGet("intents")]
    public ActionResult<List<IntentInfo>> GetSupportedIntents()
    {
        var intents = new List<IntentInfo>
        {
            new IntentInfo 
            { 
                Type = "generate_code", 
                Category = "CodeGeneration",
                Description = "Generate new code based on requirements",
                Examples = new[] { "create a user service", "build an API endpoint" }
            },
            new IntentInfo 
            { 
                Type = "explain_code", 
                Category = "CodeExplanation",
                Description = "Explain how existing code works",
                Examples = new[] { "explain this function", "what does this code do" }
            },
            new IntentInfo 
            { 
                Type = "fix_error", 
                Category = "ErrorFixing",
                Description = "Fix errors or bugs in code",
                Examples = new[] { "fix this error", "debug this function" }
            },
            new IntentInfo 
            { 
                Type = "refactor_code", 
                Category = "CodeRefactoring",
                Description = "Improve existing code",
                Examples = new[] { "refactor this method", "optimize this code" }
            },
            new IntentInfo 
            { 
                Type = "create_tests", 
                Category = "Testing",
                Description = "Generate unit or integration tests",
                Examples = new[] { "write unit tests", "create test cases" }
            },
            new IntentInfo 
            { 
                Type = "document_code", 
                Category = "Documentation",
                Description = "Add documentation to code",
                Examples = new[] { "document this function", "add comments" }
            }
        };

        return Ok(intents);
    }
}

// Request DTOs
public class ClassifyRequest
{
    public string Text { get; set; } = string.Empty;
    public string? SessionId { get; set; }
}

public class AnalyzeRequest
{
    public string Text { get; set; } = string.Empty;
    public string? Context { get; set; }
}

// Response DTOs
public class IntentInfo
{
    public string Type { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string[] Examples { get; set; } = Array.Empty<string>();
}