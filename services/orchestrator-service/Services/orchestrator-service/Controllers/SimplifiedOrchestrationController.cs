using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using VoiceCode.OrchestratorService.Models;
using VoiceCode.OrchestratorService.Services;

namespace VoiceCode.OrchestratorService.Controllers
{
    [ApiController]
    [Route("api/v2/orchestration")]
    public class SimplifiedOrchestrationController : ControllerBase
    {
        private readonly ILogger<SimplifiedOrchestrationController> _logger;
        private readonly ISimplifiedVoiceTaskService _voiceTaskService;
        private readonly IWorkerPoolService _workerPool;

        public SimplifiedOrchestrationController(
            ILogger<SimplifiedOrchestrationController> logger,
            ISimplifiedVoiceTaskService voiceTaskService,
            IWorkerPoolService workerPool)
        {
            _logger = logger;
            _voiceTaskService = voiceTaskService;
            _workerPool = workerPool;
        }

        /// <summary>
        /// Submit a voice task - Claude Code handles all planning
        /// </summary>
        [HttpPost("voice-task")]
        public async Task<ActionResult<VoiceTaskResponse>> SubmitVoiceTask([FromBody] VoiceTaskRequest request)
        {
            try
            {
                _logger.LogInformation("Received voice task: {Prompt}", request.VoicePrompt);

                // Create task from voice
                var task = await _voiceTaskService.CreateTaskFromVoiceAsync(
                    request.VoicePrompt, 
                    request.SessionId ?? Guid.NewGuid().ToString());

                // Assign to worker
                await _voiceTaskService.AssignTaskToWorkerAsync(task);

                // Execute task (Claude Code does all the work)
                var result = await _voiceTaskService.ExecuteTaskAsync(task);

                var response = new VoiceTaskResponse
                {
                    TaskId = task.TaskId,
                    Success = result.Success,
                    VoiceResponse = result.VoiceFriendlyResponse,
                    FilesCreated = result.FilesCreated,
                    FilesModified = result.FilesModified,
                    Duration = result.Duration,
                    WorkerId = task.AssignedWorkerId
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing voice task");
                return StatusCode(500, new VoiceTaskResponse
                {
                    Success = false,
                    VoiceResponse = "I encountered an error processing your request. Please try again.",
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Get task progress
        /// </summary>
        [HttpGet("task/{taskId}/progress")]
        public async Task<ActionResult<TaskProgress>> GetTaskProgress(string taskId)
        {
            try
            {
                var progress = await _voiceTaskService.GetTaskProgressAsync(taskId);
                return Ok(progress);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting task progress");
                return StatusCode(500, new { error = "Failed to get task progress" });
            }
        }

        /// <summary>
        /// Get worker pool status
        /// </summary>
        [HttpGet("workers/status")]
        public async Task<ActionResult<WorkerPoolStatus>> GetWorkerPoolStatus()
        {
            try
            {
                var status = await _workerPool.GetPoolStatusAsync();
                return Ok(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting worker pool status");
                return StatusCode(500, new { error = "Failed to get worker pool status" });
            }
        }

        /// <summary>
        /// Simple health check
        /// </summary>
        [HttpGet("health")]
        public ActionResult<object> HealthCheck()
        {
            return Ok(new
            {
                status = "healthy",
                service = "simplified-orchestrator",
                version = "2.0",
                timestamp = DateTime.UtcNow
            });
        }
    }

    // Request/Response models
    public class VoiceTaskRequest
    {
        public string VoicePrompt { get; set; }
        public string SessionId { get; set; }
        public string UserId { get; set; }
    }

    public class VoiceTaskResponse
    {
        public string TaskId { get; set; }
        public bool Success { get; set; }
        public string VoiceResponse { get; set; }
        public List<string> FilesCreated { get; set; } = new();
        public List<string> FilesModified { get; set; } = new();
        public TimeSpan Duration { get; set; }
        public string WorkerId { get; set; }
        public string Error { get; set; }
    }
}