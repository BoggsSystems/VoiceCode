using Microsoft.AspNetCore.Mvc;
using VoiceCode.OrchestratorService.Models;
using VoiceCode.OrchestratorService.Services;

namespace VoiceCode.OrchestratorService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrchestrationController : ControllerBase
{
    private readonly ILogger<OrchestrationController> _logger;
    private readonly IWorkerManagementService _workerManagement;
    private readonly ResponseTranslationService _responseTranslation;

    public OrchestrationController(
        ILogger<OrchestrationController> logger,
        IWorkerManagementService workerManagement,
        ResponseTranslationService responseTranslation)
    {
        _logger = logger;
        _workerManagement = workerManagement;
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
            var voiceResponse = await _responseTranslation.TranslateResponseAsync(
                result.Summary, 
                request.PreferredResponseStyle);

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
}