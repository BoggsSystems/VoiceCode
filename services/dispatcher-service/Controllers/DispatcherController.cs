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
            _logger.LogInformation("Executing task {TaskId} for Worker {Worker} with instructions: {Instructions}", 
                request.TaskId, request.Worker, request.Instructions);

            // Validate worker number
            if (request.Worker < 1 || request.Worker > 10) // Assuming max 10 workers
            {
                return BadRequest(new TaskExecutionResponse
                {
                    TaskId = request.TaskId,
                    Status = "failed",
                    Response = $"Invalid worker number: {request.Worker}. Please use Worker 1-10.",
                    Error = "Invalid worker number"
                });
            }

            _logger.LogInformation("Routing task to Worker {Worker}", request.Worker);

            // Create simple worker payload
            var workerPayload = new WorkerTaskPayload
            {
                TaskId = request.TaskId,
                Command = request.Instructions,
                Context = new WorkerContext
                {
                    WorkerNumber = request.Worker,
                    UserId = request.Context?.UserId,
                    SessionId = request.Context?.SessionId,
                    OriginalTranscription = request.Context?.OriginalTranscription
                },
                ResponseChannel = "worker-results"
            };

            // Send to appropriate worker queue
            var queueName = $"worker-{request.Worker}-tasks";
            
            try
            {
                var sender = _serviceBusClient.CreateSender(queueName);
                var message = new ServiceBusMessage(JsonSerializer.Serialize(workerPayload))
                {
                    SessionId = request.Context?.SessionId ?? Guid.NewGuid().ToString(),
                    MessageId = request.TaskId,
                    Subject = $"worker-{request.Worker}",
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
                Response = $"Your request is being processed by Worker {request.Worker}.",
                WorkerNumber = request.Worker
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

}

// Request/Response DTOs
public class TaskExecutionRequest
{
    public string TaskId { get; set; } = string.Empty;
    public int Worker { get; set; }
    public string Instructions { get; set; } = string.Empty;
    public TaskContext? Context { get; set; }
}

public class TaskContext
{
    public string? UserId { get; set; }
    public string? SessionId { get; set; }
    public DateTime Timestamp { get; set; }
    public string? OriginalTranscription { get; set; }
}

public class TaskExecutionResponse
{
    public string TaskId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public int? WorkerNumber { get; set; }
    public string? Error { get; set; }
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
    public int WorkerNumber { get; set; }
    public string? UserId { get; set; }
    public string? SessionId { get; set; }
    public string? OriginalTranscription { get; set; }
}