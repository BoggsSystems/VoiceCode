using Microsoft.AspNetCore.Mvc;
using VoiceCode.OrchestratorService.Models;
using VoiceCode.OrchestratorService.Services;
using VoiceCode.OrchestratorService.Configuration;

namespace VoiceCode.OrchestratorService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrchestrationController : ControllerBase
{
    private readonly ILogger<OrchestrationController> _logger;
    private readonly IWorkerManagementService _workerManagement;
    private readonly IWorkerPoolService _workerPool;
    private readonly IResponseTranslationService _responseTranslation;

    public OrchestrationController(
        ILogger<OrchestrationController> logger,
        IWorkerManagementService workerManagement,
        IWorkerPoolService workerPool,
        IResponseTranslationService responseTranslation)
    {
        _logger = logger;
        _workerManagement = workerManagement;
        _workerPool = workerPool;
        _responseTranslation = responseTranslation;
    }

    [HttpPost("submit-task")]
    public async Task<ActionResult<OrchestrationResponse>> SubmitTask([FromBody] OrchestrationRequest request)
    {
        try
        {
            _logger.LogInformation("Received orchestration request: {RequestType}", request.RequestType);

            // Create worker task
            var task = new WorkerTask
            {
                Type = request.RequestType,
                Description = request.UserPrompt,
                WorkspaceId = request.SessionId ?? Guid.NewGuid().ToString(),
                Parameters = new Dictionary<string, object>
                {
                    ["prompt"] = request.UserPrompt,
                    ["context"] = request.Context
                }
            };

            // Submit to worker pool
            var taskId = await _workerManagement.SubmitTaskAsync(task);

            // Wait for result (with timeout)
            var result = await _workerManagement.GetTaskResultAsync(taskId, TimeSpan.FromMinutes(5));

            if (result == null)
            {
                return StatusCode(504, new { error = "Task execution timeout" });
            }

            // Translate result for voice
            var voiceResponse = await _responseTranslation.TranslateClaudeResponseAsync(
                result.Summary, 
                request.UserPrompt ?? request.Transcript,
                request.RequestedDetailLevel ?? ResponseDetailLevel.Summary);

            var response = new OrchestrationResponse
            {
                Success = result.Success,
                Message = voiceResponse.VoiceFriendlyDescription,
                KeyActions = voiceResponse.KeyActions,
                NextSteps = voiceResponse.NextSteps,
                RequiresConfirmation = voiceResponse.RequiresConfirmation,
                ConfirmationPrompt = voiceResponse.ConfirmationPrompt,
                Metadata = new Dictionary<string, object>
                {
                    ["TaskId"] = taskId,
                    ["FileOperations"] = result.FileOperations.Count,
                    ["CommandExecutions"] = result.CommandExecutions.Count,
                    ["WorkerId"] = result.Metadata.GetValueOrDefault("WorkerId", "unknown")
                }
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing orchestration request");
            return StatusCode(500, new { error = "Failed to process request" });
        }
    }

    [HttpGet("workers/status")]
    public async Task<ActionResult<List<WorkerStatus>>> GetWorkerStatuses()
    {
        try
        {
            var statuses = await _workerManagement.GetWorkerStatusesAsync();
            return Ok(statuses);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting worker statuses");
            return StatusCode(500, new { error = "Failed to get worker statuses" });
        }
    }

    [HttpGet("workers/available")]
    public async Task<ActionResult<bool>> IsWorkerAvailable()
    {
        try
        {
            var available = await _workerManagement.IsWorkerAvailableAsync();
            return Ok(new { available });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking worker availability");
            return StatusCode(500, new { error = "Failed to check worker availability" });
        }
    }

    [HttpGet("health")]
    public ActionResult<object> HealthCheck()
    {
        return Ok(new 
        { 
            status = "healthy",
            service = "orchestrator-service",
            timestamp = DateTime.UtcNow
        });
    }

    // Multi-Worker Management Endpoints

    [HttpGet("pool/status")]
    public async Task<ActionResult<WorkerPoolStatus>> GetPoolStatus()
    {
        try
        {
            var status = await _workerPool.GetPoolStatusAsync();
            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pool status");
            return StatusCode(500, new { error = "Failed to get pool status" });
        }
    }

    [HttpPost("pool/register")]
    public async Task<ActionResult<WorkerRegistration>> RegisterWorker([FromBody] WorkerRegistrationRequest request)
    {
        try
        {
            var registration = await _workerPool.RegisterWorkerAsync(request.Endpoint, request.Capabilities);
            return Ok(registration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering worker");
            return StatusCode(500, new { error = "Failed to register worker" });
        }
    }

    [HttpDelete("pool/worker/{workerId}")]
    public async Task<ActionResult> UnregisterWorker(string workerId)
    {
        try
        {
            var success = await _workerPool.UnregisterWorkerAsync(workerId);
            if (success)
            {
                return Ok(new { message = $"Worker {workerId} unregistered successfully" });
            }
            return NotFound(new { error = $"Worker {workerId} not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unregistering worker");
            return StatusCode(500, new { error = "Failed to unregister worker" });
        }
    }

    [HttpGet("pool/metrics")]
    public async Task<ActionResult<Dictionary<string, WorkerMetrics>>> GetWorkerMetrics()
    {
        try
        {
            var metrics = await _workerPool.GetAllWorkerMetricsAsync();
            return Ok(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting worker metrics");
            return StatusCode(500, new { error = "Failed to get worker metrics" });
        }
    }

    [HttpGet("pool/assignments")]
    public async Task<ActionResult<Dictionary<string, List<string>>>> GetTaskAssignments()
    {
        try
        {
            var assignments = await _workerPool.GetActiveTaskAssignmentsAsync();
            return Ok(assignments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting task assignments");
            return StatusCode(500, new { error = "Failed to get task assignments" });
        }
    }

    [HttpPost("pool/scale")]
    public async Task<ActionResult> ScaleWorkers([FromBody] ScaleRequest request)
    {
        try
        {
            await _workerPool.ScaleWorkersAsync(request.TargetCount);
            return Ok(new { message = $"Scaling to {request.TargetCount} workers initiated" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scaling workers");
            return StatusCode(500, new { error = "Failed to scale workers" });
        }
    }

    [HttpGet("pool/policy")]
    public async Task<ActionResult<TaskDistributionPolicy>> GetDistributionPolicy()
    {
        try
        {
            var policy = await _workerPool.GetDistributionPolicyAsync();
            return Ok(policy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting distribution policy");
            return StatusCode(500, new { error = "Failed to get distribution policy" });
        }
    }

    [HttpPut("pool/policy")]
    public async Task<ActionResult> UpdateDistributionPolicy([FromBody] TaskDistributionPolicy policy)
    {
        try
        {
            await _workerPool.UpdateDistributionPolicyAsync(policy);
            return Ok(new { message = "Distribution policy updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating distribution policy");
            return StatusCode(500, new { error = "Failed to update distribution policy" });
        }
    }

    [HttpPost("submit-batch")]
    public async Task<ActionResult<BatchOrchestrationResponse>> SubmitBatchTasks([FromBody] List<OrchestrationRequest> requests)
    {
        try
        {
            _logger.LogInformation("Received batch orchestration request with {Count} tasks", requests.Count);

            var tasks = new List<Task<(string TaskId, OrchestrationResponse Response)>>();

            foreach (var request in requests)
            {
                tasks.Add(ProcessTaskAsync(request));
            }

            var results = await Task.WhenAll(tasks);

            var response = new BatchOrchestrationResponse
            {
                TotalTasks = requests.Count,
                SuccessfulTasks = results.Count(r => r.Response.Success),
                FailedTasks = results.Count(r => !r.Response.Success),
                Results = results.ToDictionary(r => r.TaskId, r => r.Response)
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing batch orchestration request");
            return StatusCode(500, new { error = "Failed to process batch request" });
        }
    }

    private async Task<(string TaskId, OrchestrationResponse Response)> ProcessTaskAsync(OrchestrationRequest request)
    {
        try
        {
            var task = new WorkerTask
            {
                Type = request.RequestType,
                Description = request.UserPrompt,
                WorkspaceId = request.SessionId ?? Guid.NewGuid().ToString(),
                Parameters = new Dictionary<string, object>
                {
                    ["prompt"] = request.UserPrompt,
                    ["context"] = request.Context
                }
            };

            var taskId = await _workerManagement.SubmitTaskAsync(task);
            var result = await _workerManagement.GetTaskResultAsync(taskId, TimeSpan.FromMinutes(5));

            if (result == null)
            {
                return (taskId, new OrchestrationResponse 
                { 
                    Success = false, 
                    Message = "Task execution timeout" 
                });
            }

            var voiceResponse = await _responseTranslation.TranslateClaudeResponseAsync(
                result.Summary, 
                request.UserPrompt ?? request.Transcript,
                request.RequestedDetailLevel ?? ResponseDetailLevel.Summary);

            var response = new OrchestrationResponse
            {
                Success = result.Success,
                Message = voiceResponse.VoiceFriendlyDescription,
                KeyActions = voiceResponse.KeyActions,
                NextSteps = voiceResponse.NextSteps,
                RequiresConfirmation = voiceResponse.RequiresConfirmation,
                ConfirmationPrompt = voiceResponse.ConfirmationPrompt,
                Metadata = new Dictionary<string, object>
                {
                    ["TaskId"] = taskId,
                    ["FileOperations"] = result.FileOperations.Count,
                    ["CommandExecutions"] = result.CommandExecutions.Count,
                    ["WorkerId"] = result.Metadata.GetValueOrDefault("WorkerId", "unknown")
                }
            };

            return (taskId, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing task");
            return (Guid.NewGuid().ToString(), new OrchestrationResponse 
            { 
                Success = false, 
                Message = "Failed to process task" 
            });
        }
    }
}

// Additional request/response models
public class WorkerRegistrationRequest
{
    public string Endpoint { get; set; }
    public WorkerCapabilities Capabilities { get; set; }
}

public class ScaleRequest
{
    public int TargetCount { get; set; }
}

public class BatchOrchestrationResponse
{
    public int TotalTasks { get; set; }
    public int SuccessfulTasks { get; set; }
    public int FailedTasks { get; set; }
    public Dictionary<string, OrchestrationResponse> Results { get; set; }
}