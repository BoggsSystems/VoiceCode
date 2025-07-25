using Microsoft.Extensions.Options;
using VoiceCode.WorkerService.Configuration;
using VoiceCode.WorkerService.Models;

namespace VoiceCode.WorkerService.Services;

public interface IClaudeCodeWorkerService
{
    Task<WorkerTaskResult> ExecuteTaskAsync(WorkerTask task);
    Task<WorkerStatus> GetStatusAsync();
    Task<bool> InitializeWorkspaceAsync(string workspaceId);
    Task CleanupWorkspaceAsync(string workspaceId);
}

public class ClaudeCodeWorkerService : IClaudeCodeWorkerService
{
    private readonly ILogger<ClaudeCodeWorkerService> _logger;
    private readonly IClaudeCodeCliService _claudeCodeCli;
    private readonly WorkerOptions _options;
    private readonly WorkerStatus _status;
    
    public ClaudeCodeWorkerService(
        ILogger<ClaudeCodeWorkerService> logger,
        IClaudeCodeCliService claudeCodeCli,
        IOptions<WorkerOptions> options)
    {
        _logger = logger;
        _claudeCodeCli = claudeCodeCli;
        _options = options.Value;
        _status = new WorkerStatus
        {
            WorkerId = Environment.GetEnvironmentVariable("WORKER_ID") ?? Guid.NewGuid().ToString(),
            State = WorkerState.Idle
        };
    }

    public async Task<WorkerTaskResult> ExecuteTaskAsync(WorkerTask task)
    {
        _logger.LogInformation("Executing task {TaskId}: {Description}", task.Id, task.Description);
        
        var result = new WorkerTaskResult
        {
            TaskId = task.Id,
            Success = false
        };

        try
        {
            _status.State = WorkerState.Busy;
            _status.CurrentTaskId = task.Id;
            task.StartedAt = DateTime.UtcNow;

            // Initialize workspace
            await InitializeWorkspaceAsync(task.WorkspaceId);

            // Extract voice command from parameters
            var voiceCommand = task.Parameters.GetValueOrDefault("voiceCommand")?.ToString();
            
            if (string.IsNullOrEmpty(voiceCommand))
            {
                throw new ArgumentException("Voice command is required");
            }
            
            _logger.LogInformation("Executing Claude Code CLI with command: {VoiceCommand}", voiceCommand);
            
            // Execute the voice command using Claude Code CLI
            var claudeResult = await _claudeCodeCli.ExecuteCommandAsync(voiceCommand, task.WorkspaceId);
            
            // Log Claude's output immediately
            _logger.LogInformation("Claude Code execution completed for task {TaskId}:\n" +
                "Exit Code: {ExitCode}\n" +
                "Success: {Success}\n" +
                "Output:\n{Output}\n" +
                "Error:\n{Error}",
                task.Id, claudeResult.ExitCode, claudeResult.Success, 
                claudeResult.Output, claudeResult.Error);
            
            // Build the result
            result = new WorkerTaskResult
            {
                TaskId = task.Id,
                Success = claudeResult.Success,
                Summary = claudeResult.Output,
                Error = claudeResult.Error,
                Metadata = new Dictionary<string, object>
                {
                    ["voiceCommand"] = voiceCommand,
                    ["workerId"] = _status.WorkerId,
                    ["exitCode"] = claudeResult.ExitCode,
                    ["timestamp"] = DateTime.UtcNow
                }
            };
            
            if (!claudeResult.Success)
            {
                _logger.LogWarning("Claude Code execution failed with exit code {ExitCode}: {Error}", 
                    claudeResult.ExitCode, claudeResult.Error);
            }

            task.CompletedAt = DateTime.UtcNow;
            task.Status = WorkerTaskStatus.Completed;
            _status.CompletedTasks++;
            
            _logger.LogInformation("Task {TaskId} completed successfully", task.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing task {TaskId}", task.Id);
            task.Status = WorkerTaskStatus.Failed;
            task.Error = ex.Message;
            result.Success = false;
            result.Error = ex.Message;
            _status.FailedTasks++;
        }
        finally
        {
            _status.State = WorkerState.Idle;
            _status.CurrentTaskId = null;
            _status.LastHeartbeat = DateTime.UtcNow;
        }

        return result;
    }

    public Task<WorkerStatus> GetStatusAsync()
    {
        _status.LastHeartbeat = DateTime.UtcNow;
        return Task.FromResult(_status);
    }

    public Task<bool> InitializeWorkspaceAsync(string workspaceId)
    {
        var workspaceDir = Path.Combine(_options.WorkspaceBasePath, workspaceId);
        
        if (!Directory.Exists(workspaceDir))
        {
            Directory.CreateDirectory(workspaceDir);
            _logger.LogInformation("Created workspace directory: {WorkspaceDir}", workspaceDir);
        }
        
        return Task.FromResult(true);
    }

    public Task CleanupWorkspaceAsync(string workspaceId)
    {
        var workspaceDir = Path.Combine(_options.WorkspaceBasePath, workspaceId);
        
        if (Directory.Exists(workspaceDir))
        {
            try
            {
                Directory.Delete(workspaceDir, true);
                _logger.LogInformation("Cleaned up workspace: {WorkspaceId}", workspaceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cleanup workspace: {WorkspaceId}", workspaceId);
            }
        }
        
        return Task.CompletedTask;
    }
}