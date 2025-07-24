using Microsoft.AspNetCore.Mvc;
using VoiceCode.WorkerService.Models;
using VoiceCode.WorkerService.Services;

namespace VoiceCode.WorkerService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WorkerController : ControllerBase
{
    private readonly ILogger<WorkerController> _logger;
    private readonly IClaudeCodeWorkerService _workerService;
    private readonly IMcpClientService _mcpClient;

    public WorkerController(
        ILogger<WorkerController> logger,
        IClaudeCodeWorkerService workerService,
        IMcpClientService mcpClient)
    {
        _logger = logger;
        _workerService = workerService;
        _mcpClient = mcpClient;
    }

    [HttpGet("status")]
    public async Task<ActionResult<WorkerStatus>> GetStatus()
    {
        try
        {
            var status = await _workerService.GetStatusAsync();
            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting worker status");
            return StatusCode(500, new { error = "Failed to get worker status" });
        }
    }

    [HttpPost("execute")]
    public async Task<ActionResult<WorkerTaskResult>> ExecuteTask([FromBody] WorkerTask task)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var result = await _workerService.ExecuteTaskAsync(task);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing task");
            return StatusCode(500, new { error = "Failed to execute task" });
        }
    }

    [HttpGet("tools")]
    public async Task<ActionResult<List<Tool>>> GetAvailableTools()
    {
        try
        {
            if (!_mcpClient.IsConnected)
            {
                await _mcpClient.ConnectAsync();
            }

            var tools = await _mcpClient.GetToolsAsync();
            return Ok(tools);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available tools");
            return StatusCode(500, new { error = "Failed to get tools" });
        }
    }

    [HttpPost("workspace/{workspaceId}/init")]
    public async Task<ActionResult> InitializeWorkspace(string workspaceId)
    {
        try
        {
            var success = await _workerService.InitializeWorkspaceAsync(workspaceId);
            if (success)
            {
                return Ok(new { message = $"Workspace {workspaceId} initialized successfully" });
            }
            
            return StatusCode(500, new { error = "Failed to initialize workspace" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing workspace");
            return StatusCode(500, new { error = "Failed to initialize workspace" });
        }
    }

    [HttpDelete("workspace/{workspaceId}")]
    public async Task<ActionResult> CleanupWorkspace(string workspaceId)
    {
        try
        {
            await _workerService.CleanupWorkspaceAsync(workspaceId);
            return Ok(new { message = $"Workspace {workspaceId} cleaned up successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up workspace");
            return StatusCode(500, new { error = "Failed to cleanup workspace" });
        }
    }

    [HttpGet("health")]
    public ActionResult<object> HealthCheck()
    {
        return Ok(new 
        { 
            status = "healthy",
            service = "worker-service",
            timestamp = DateTime.UtcNow,
            mcpConnected = _mcpClient.IsConnected
        });
    }
}