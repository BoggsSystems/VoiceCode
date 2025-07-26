using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Azure.Messaging.ServiceBus;
using System.Text.Json;
using VoiceCode.DispatcherService.Services;

namespace VoiceCode.DispatcherService.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class DispatcherController : ControllerBase
{
    private readonly ILogger<DispatcherController> _logger;
    private readonly IQueueDispatcher _queueDispatcher;
    private readonly ServiceBusClient _serviceBusClient;
    private readonly IConfiguration _configuration;

    public DispatcherController(
        ILogger<DispatcherController> logger,
        IQueueDispatcher queueDispatcher,
        ServiceBusClient serviceBusClient,
        IConfiguration configuration)
    {
        _logger = logger;
        _queueDispatcher = queueDispatcher;
        _serviceBusClient = serviceBusClient;
        _configuration = configuration;
    }

    [HttpPost("execute-task")]
    public async Task<IActionResult> ExecuteTask([FromBody] TaskExecutionRequest request)
    {
        try
        {
            _logger.LogInformation("Executing task {TaskId} with intent {Intent}", 
                request.TaskId, request.Intent);

            // Handle clarification requests
            if (request.Intent == "unclear" && request.Metadata?.ContainsKey("clarification_needed") == true)
            {
                return Ok(new TaskExecutionResponse
                {
                    TaskId = request.TaskId,
                    Status = "clarification_needed",
                    Response = request.Metadata["clarification_needed"].ToString(),
                    RequiresClarification = true
                });
            }

            // Determine which worker should handle this task
            var workerType = DetermineWorkerType(request);
            
            if (string.IsNullOrEmpty(workerType))
            {
                return BadRequest(new TaskExecutionResponse
                {
                    TaskId = request.TaskId,
                    Status = "failed",
                    Response = "Could not determine appropriate worker for this task",
                    Error = "No suitable worker found"
                });
            }

            _logger.LogInformation("Routing task to {WorkerType} worker", workerType);

            // Create simple worker payload
            var workerPayload = new WorkerTaskPayload
            {
                TaskId = request.TaskId,
                Command = request.OriginalRequest,
                Context = new WorkerContext
                {
                    Product = request.Metadata?.GetValueOrDefault("product")?.ToString(),
                    Intent = request.Intent,
                    UserId = request.Context?.UserId,
                    SessionId = request.Context?.SessionId,
                    Metadata = request.Metadata ?? new Dictionary<string, object>()
                },
                ResponseChannel = "worker-results"
            };

            // Send to appropriate worker queue
            var queueName = $"worker-{workerType}-tasks";
            
            try
            {
                var sender = _serviceBusClient.CreateSender(queueName);
                var message = new ServiceBusMessage(JsonSerializer.Serialize(workerPayload))
                {
                    SessionId = request.Context?.SessionId ?? Guid.NewGuid().ToString(),
                    MessageId = request.TaskId,
                    Subject = request.Intent,
                    ContentType = "application/json",
                    TimeToLive = TimeSpan.FromMinutes(5)
                };
                
                await sender.SendMessageAsync(message);
                await sender.DisposeAsync();
                
                _logger.LogInformation("Task {TaskId} dispatched to queue {QueueName}", 
                    request.TaskId, queueName);
            }
            catch (ServiceBusException sbEx)
            {
                _logger.LogError(sbEx, "Failed to send message to queue {QueueName}", queueName);
                throw;
            }

            return Accepted(new TaskExecutionResponse
            {
                TaskId = request.TaskId,
                Status = "processing",
                Response = $"Your request is being processed by the {workerType} team.",
                WorkerType = workerType
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing task {TaskId}", request.TaskId);
            return StatusCode(500, new TaskExecutionResponse
            {
                TaskId = request.TaskId,
                Status = "failed",
                Response = "An error occurred while processing your request",
                Error = ex.Message
            });
        }
    }

    private string DetermineWorkerType(TaskExecutionRequest request)
    {
        // Use product from metadata if available
        if (request.Metadata?.TryGetValue("product", out var product) == true && product != null)
        {
            var productName = product.ToString()?.ToLowerInvariant();
            return productName switch
            {
                "voicecode" => "voicecode",
                "financetracker" => "financetracker",
                "healthmonitor" => "healthmonitor",
                "edulearn" => "edulearn",
                "gamehub" => "gamehub",
                _ => "general"
            };
        }

        // Fall back to intent-based routing if no specific product
        return request.Intent switch
        {
            "create_feature" => "development",
            "fix_bug" => "development",
            "refactor_code" => "development",
            "add_tests" => "testing",
            "documentation" => "documentation",
            "deploy" => "devops",
            _ => "general"
        };
    }
}

// Request/Response DTOs
public class TaskExecutionRequest
{
    public string TaskId { get; set; } = string.Empty;
    public string OriginalRequest { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    public TaskContext? Context { get; set; }
}

public class TaskContext
{
    public string? UserId { get; set; }
    public string? SessionId { get; set; }
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object>? AudioMetadata { get; set; }
}

public class TaskExecutionResponse
{
    public string TaskId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public string? WorkerType { get; set; }
    public string? Error { get; set; }
    public bool RequiresClarification { get; set; }
}

// Simple worker payload
public class WorkerTaskPayload
{
    public string TaskId { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public WorkerContext Context { get; set; } = new();
    public string ResponseChannel { get; set; } = string.Empty;
}

public class WorkerContext
{
    public string? Product { get; set; }
    public string? Intent { get; set; }
    public string? UserId { get; set; }
    public string? SessionId { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}