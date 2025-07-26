using Microsoft.Extensions.Options;
using VoiceCode.WorkerService.Configuration;
using VoiceCode.WorkerService.Models;
using System.Text;

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
    private readonly IClaudeApiService _claudeApi;
    private readonly IFileOperationExecutor _fileOperationExecutor;
    private readonly IRepositoryAnalyzer _repositoryAnalyzer;
    private readonly WorkerOptions _options;
    private readonly WorkerStatus _status;
    
    public ClaudeCodeWorkerService(
        ILogger<ClaudeCodeWorkerService> logger,
        IClaudeApiService claudeApi,
        IFileOperationExecutor fileOperationExecutor,
        IRepositoryAnalyzer repositoryAnalyzer,
        IOptions<WorkerOptions> options)
    {
        _logger = logger;
        _claudeApi = claudeApi;
        _fileOperationExecutor = fileOperationExecutor;
        _repositoryAnalyzer = repositoryAnalyzer;
        _options = options.Value;
        _status = new WorkerStatus
        {
            WorkerId = Environment.GetEnvironmentVariable("WORKER_ID") ?? Guid.NewGuid().ToString(),
            State = WorkerState.Idle
        };
    }

    public async Task<WorkerTaskResult> ExecuteTaskAsync(WorkerTask task)
    {
        _logger.LogInformation("=== CLAUDE CODE WORKER EXECUTION STARTED ===");
        _logger.LogInformation("Task ID: {TaskId}", task.Id);
        _logger.LogInformation("Task Type: {Type}", task.Type);
        _logger.LogInformation("Description: {Description}", task.Description);
        _logger.LogInformation("Workspace ID: {WorkspaceId}", task.WorkspaceId);
        _logger.LogInformation("Parameters: {Parameters}", string.Join(", ", task.Parameters.Select(p => $"{p.Key}={p.Value}")));
        
        var result = new WorkerTaskResult
        {
            TaskId = task.Id,
            Success = false
        };

        try
        {
            _status.State = WorkerState.Busy;
            _status.CurrentTaskId = task.Id;
            _logger.LogInformation("Worker state changed to BUSY");
            task.StartedAt = DateTime.UtcNow;

            // Initialize workspace
            await InitializeWorkspaceAsync(task.WorkspaceId);

            // Extract voice command from parameters
            _logger.LogInformation("Extracting voice command from parameters...");
            var voiceCommand = task.Parameters.GetValueOrDefault("voiceCommand")?.ToString();
            _logger.LogInformation("Voice command extracted: {VoiceCommand}", voiceCommand ?? "null");
            
            if (string.IsNullOrEmpty(voiceCommand))
            {
                _logger.LogError("Voice command is null or empty. Parameters: {Parameters}", 
                    string.Join(", ", task.Parameters.Select(p => $"{p.Key}={p.Value}")));
                throw new ArgumentException("Voice command is required");
            }
            
            // Analyze repository context
            _logger.LogInformation("Analyzing repository context...");
            var workspaceRoot = "/workspace"; // Default workspace location
            var repoContext = await _repositoryAnalyzer.AnalyzeRepositoryAsync(workspaceRoot);
            _logger.LogInformation("Repository analyzed - Type: {Type}, Files: {FileCount}", 
                repoContext.ProjectType, repoContext.RelevantFiles.Count);
            
            // Generate file operations using Claude API
            _logger.LogInformation("Calling Claude API to generate file operations...");
            var fileOperations = await _claudeApi.GenerateFileOperationsAsync(voiceCommand, repoContext);
            _logger.LogInformation("Claude API returned {Count} operations", fileOperations.Operations.Count);
            
            // Execute file operations
            _logger.LogInformation("Executing file operations...");
            var executionResult = await _fileOperationExecutor.ExecuteOperationsAsync(
                fileOperations.Operations, 
                workspaceRoot);
            
            _logger.LogInformation("File operations completed: {Success}/{Total} successful",
                executionResult.SuccessfulOperations, executionResult.TotalOperations);
            
            // Build comprehensive result
            var summaryBuilder = new System.Text.StringBuilder();
            summaryBuilder.AppendLine(fileOperations.Summary);
            
            if (executionResult.FailedOperations > 0)
            {
                summaryBuilder.AppendLine($"\nNote: {executionResult.FailedOperations} operations failed.");
            }
            
            result = new WorkerTaskResult
            {
                TaskId = task.Id,
                Success = executionResult.FailedOperations == 0,
                Summary = summaryBuilder.ToString(),
                Error = executionResult.FailedOperations > 0 
                    ? string.Join("; ", executionResult.ExecutedOperations
                        .Where(op => !op.Success)
                        .Select(op => $"{op.Operation.Path}: {op.Error}"))
                    : null,
                FileOperations = executionResult.ExecutedOperations.Select(op => new FileOperation
                {
                    Type = op.Operation.Type,
                    FilePath = op.Operation.Path,
                    Content = op.ResultContent,
                    OldContent = op.OriginalContent
                }).ToList(),
                Metadata = new Dictionary<string, object>
                {
                    ["voiceCommand"] = voiceCommand,
                    ["workerId"] = _status.WorkerId,
                    ["projectType"] = repoContext.ProjectType,
                    ["operationsRequested"] = fileOperations.Operations.Count,
                    ["operationsSucceeded"] = executionResult.SuccessfulOperations,
                    ["timestamp"] = DateTime.UtcNow
                }
            };
            
            if (!result.Success)
            {
                _logger.LogWarning("Some file operations failed. See error details.");
            }

            task.CompletedAt = DateTime.UtcNow;
            task.Status = WorkerTaskStatus.Completed;
            _status.CompletedTasks++;
            
            _logger.LogInformation("Task {TaskId} completed successfully", task.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "=== ERROR IN CLAUDE CODE WORKER ===");
            _logger.LogError("Task ID: {TaskId}", task.Id);
            _logger.LogError("Exception Type: {ExceptionType}", ex.GetType().Name);
            _logger.LogError("Exception Message: {ExceptionMessage}", ex.Message);
            _logger.LogError("Stack Trace: {StackTrace}", ex.StackTrace);
            
            task.Status = WorkerTaskStatus.Failed;
            task.Error = ex.Message;
            result.Success = false;
            result.Error = ex.Message;
            result.Summary = $"Error: {ex.Message}";
            _status.FailedTasks++;
        }
        finally
        {
            _status.State = WorkerState.Idle;
            _status.CurrentTaskId = null;
            _status.LastHeartbeat = DateTime.UtcNow;
            _logger.LogInformation("Worker state changed back to IDLE");
            _logger.LogInformation("=== CLAUDE CODE WORKER EXECUTION COMPLETED ===");
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