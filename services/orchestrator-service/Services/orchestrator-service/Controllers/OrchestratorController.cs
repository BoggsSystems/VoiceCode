using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VoiceCode.OrchestratorService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrchestratorController : ControllerBase
{
    private readonly ServiceBusClient _serviceBusClient;
    private readonly IHubContext<TaskProgressHub> _hubContext;
    private readonly ILogger<OrchestratorController> _logger;
    private static readonly Regex WorkerPattern = new(@"worker[- ]?(\d+)", RegexOptions.IgnoreCase);

    public OrchestratorController(
        ServiceBusClient serviceBusClient,
        IHubContext<TaskProgressHub> hubContext,
        ILogger<OrchestratorController> logger)
    {
        _serviceBusClient = serviceBusClient;
        _hubContext = hubContext;
        _logger = logger;
    }

    [HttpPost("submit")]
    public async Task<IActionResult> SubmitTask([FromBody] VoiceTaskRequest request)
    {
        try
        {
            // Extract worker number from the voice command
            var workerId = ExtractWorkerId(request.VoiceCommand);
            
            // If no worker specified, check session context
            if (workerId == null)
            {
                workerId = HttpContext.Session.GetString("CurrentWorkerId");
            }

            // If still no worker, return error
            if (string.IsNullOrEmpty(workerId))
            {
                return BadRequest(new { error = "No worker specified. Please mention a worker number (e.g., 'worker 3')." });
            }

            // Update session context
            HttpContext.Session.SetString("CurrentWorkerId", workerId);

            // Create task
            var task = new WorkerTask
            {
                TaskId = Guid.NewGuid().ToString(),
                WorkerId = workerId,
                VoiceCommand = request.VoiceCommand,
                SessionId = request.SessionId ?? HttpContext.Session.Id,
                Timestamp = DateTime.UtcNow
            };

            // Send to worker's queue
            var queueName = $"{workerId}-tasks";
            await SendToQueue(queueName, task);

            // Notify via SignalR
            await _hubContext.Clients.All.SendAsync("TaskSubmitted", task.TaskId, workerId);

            _logger.LogInformation($"Task {task.TaskId} submitted to {workerId}");

            return Ok(new
            {
                taskId = task.TaskId,
                workerId = workerId,
                status = "submitted",
                message = $"Task submitted to {workerId}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting task");
            return StatusCode(500, new { error = "Failed to submit task", details = ex.Message });
        }
    }

    [HttpGet("task/{taskId}")]
    public async Task<IActionResult> GetTaskStatus(string taskId)
    {
        // In a real implementation, this would check task status from storage
        // For now, return a simple response
        return Ok(new
        {
            taskId = taskId,
            status = "processing",
            message = "Task is being processed by Claude Code"
        });
    }

    [HttpGet("context")]
    public IActionResult GetCurrentContext()
    {
        var currentWorkerId = HttpContext.Session.GetString("CurrentWorkerId");
        return Ok(new
        {
            currentWorkerId = currentWorkerId,
            hasContext = !string.IsNullOrEmpty(currentWorkerId)
        });
    }

    [HttpPost("context/clear")]
    public IActionResult ClearContext()
    {
        HttpContext.Session.Clear();
        return Ok(new { message = "Context cleared" });
    }

    private string? ExtractWorkerId(string voiceCommand)
    {
        var match = WorkerPattern.Match(voiceCommand);
        if (match.Success)
        {
            var workerNumber = match.Groups[1].Value;
            return $"worker-{workerNumber}";
        }
        return null;
    }

    private async Task SendToQueue(string queueName, WorkerTask task)
    {
        var sender = _serviceBusClient.CreateSender(queueName);
        var message = new ServiceBusMessage(JsonSerializer.Serialize(task))
        {
            ContentType = "application/json",
            Subject = "VoiceTask",
            MessageId = task.TaskId
        };

        await sender.SendMessageAsync(message);
        await sender.DisposeAsync();
    }
}

// Request/Response models
public class VoiceTaskRequest
{
    public string VoiceCommand { get; set; } = "";
    public string? SessionId { get; set; }
}

public class WorkerTask
{
    public string TaskId { get; set; } = "";
    public string WorkerId { get; set; } = "";
    public string VoiceCommand { get; set; } = "";
    public string SessionId { get; set; } = "";
    public DateTime Timestamp { get; set; }
}