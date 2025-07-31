using Microsoft.Extensions.Options;
using VoiceCode.WorkerService.Configuration;
using VoiceCode.WorkerService.Models;
using System.Text;
using System.Linq;

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
    private readonly IClaudeCodeSidecarClient _sidecarClient;
    private readonly IFileOperationExecutor _fileOperationExecutor;
    private readonly IRepositoryAnalyzer _repositoryAnalyzer;
    private readonly WorkerOptions _options;
    private readonly WorkerStatus _status;
    private readonly Dictionary<string, string> _sessionMap = new();
    
    public ClaudeCodeWorkerService(
        ILogger<ClaudeCodeWorkerService> logger,
        IClaudeApiService claudeApi,
        IClaudeCodeSidecarClient sidecarClient,
        IFileOperationExecutor fileOperationExecutor,
        IRepositoryAnalyzer repositoryAnalyzer,
        IOptions<WorkerOptions> options)
    {
        _logger = logger;
        _claudeApi = claudeApi;
        _sidecarClient = sidecarClient;
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
            var sessionId = task.Parameters.GetValueOrDefault("sessionId")?.ToString() ?? task.Id;
            _logger.LogInformation("Voice command extracted: {VoiceCommand}", voiceCommand ?? "null");
            
            if (string.IsNullOrEmpty(voiceCommand))
            {
                _logger.LogError("Voice command is null or empty. Parameters: {Parameters}", 
                    string.Join(", ", task.Parameters.Select(p => $"{p.Key}={p.Value}")));
                throw new ArgumentException("Voice command is required");
            }

            // Context is now handled at the sidecar level
            _logger.LogInformation("Using sidecar client - SessionId: {SessionId}, WorkerId: {WorkerId}", 
                sessionId, _status.WorkerId);

            // Determine whether to use SDK or API based on task complexity
            var useSdk = ShouldUseSdk(voiceCommand, task);
            _logger.LogInformation("Execution mode: {Mode}", useSdk ? "Claude Code SDK" : "Direct API");

            if (useSdk)
            {
                // Execute using Claude Code SDK
                result = await ExecuteWithSdkAsync(task, voiceCommand, sessionId);
            }
            else
            {
                // Fall back to original API implementation
                result = await ExecuteWithApiAsync(task, voiceCommand);
            }
            
            if (!result.Success)
            {
                _logger.LogWarning("Some operations failed. See error details.");
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

    public async Task CleanupWorkspaceAsync(string workspaceId)
    {
        // Clean up any SDK sessions for this workspace
        var sessionsToRemove = _sessionMap.Where(kvp => kvp.Key.Contains(workspaceId)).ToList();
        foreach (var session in sessionsToRemove)
        {
            await _sidecarClient.CloseSessionAsync(session.Value);
            _sessionMap.Remove(session.Key);
        }

        // Clean up workspace directory
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
    }

    private bool ShouldUseSdk(string voiceCommand, WorkerTask task)
    {
        // Use SDK for more complex operations that benefit from its features
        var sdkKeywords = new[] { "create", "build", "implement", "add", "update", "refactor", "generate", "scaffold" };
        var usesSdkKeyword = sdkKeywords.Any(keyword => voiceCommand.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        
        // Check if task explicitly requests SDK
        var preferSdk = task.Parameters.GetValueOrDefault("useSdk")?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;
        
        return usesSdkKeyword || preferSdk;
    }

    private async Task<WorkerTaskResult> ExecuteWithSdkAsync(WorkerTask task, string voiceCommand, string sessionId)
    {
        _logger.LogInformation("=== EXECUTING WITH CLAUDE CODE SDK ===");
        
        var result = new WorkerTaskResult
        {
            TaskId = task.Id,
            Success = false
        };

        try
        {
            // Check if SDK is available
            if (!await _sidecarClient.IsAvailableAsync())
            {
                _logger.LogWarning("Claude Code SDK not available, falling back to API");
                return await ExecuteWithApiAsync(task, voiceCommand);
            }

            var workspaceRoot = Path.Combine(_options.WorkspaceBasePath, task.WorkspaceId);
            
            // Create or reuse session
            if (!_sessionMap.ContainsKey(sessionId))
            {
                var sdkSessionId = await _sidecarClient.CreateSessionAsync(task.WorkspaceId, new Dictionary<string, object>
                {
                    ["voiceCommand"] = voiceCommand,
                    ["workerId"] = _status.WorkerId
                });
                _sessionMap[sessionId] = sdkSessionId;
                _logger.LogInformation("Created new SDK session: {SdkSessionId}", sdkSessionId);
            }

            // Analyze repository for context
            var repoContext = await _repositoryAnalyzer.AnalyzeRepositoryAsync(workspaceRoot);
            
            // Build SDK prompt with context
            var sdkPrompt = BuildSdkPrompt(voiceCommand, repoContext);
            
            // Execute with streaming for real-time feedback
            var progressMessages = new List<string>();
            var sdkResult = await _sidecarClient.ExecuteWithStreamingAsync(
                sdkPrompt,
                progress => {
                    progressMessages.Add(progress);
                    _logger.LogDebug("SDK Progress: {Progress}", progress);
                },
                workspaceRoot
            );

            if (sdkResult.Success)
            {
                // Parse SDK output to extract file operations
                var fileOperations = ParseSdkOutput(sdkResult, progressMessages);
                
                result = new WorkerTaskResult
                {
                    TaskId = task.Id,
                    Success = true,
                    Summary = GenerateSummary(sdkResult, fileOperations),
                    FileOperations = fileOperations,
                    Metadata = new Dictionary<string, object>
                    {
                        ["voiceCommand"] = voiceCommand,
                        ["workerId"] = _status.WorkerId,
                        ["executionMode"] = "SDK",
                        ["sessionId"] = sessionId,
                        ["executionTime"] = sdkResult.ExecutionTime.TotalSeconds,
                        ["streamUpdates"] = sdkResult.StreamUpdates?.Count ?? 0,
                        ["timestamp"] = DateTime.UtcNow
                    }
                };

                if (sdkResult.Metadata != null)
                {
                    result.Metadata["tokensUsed"] = sdkResult.Metadata.TokensUsed;
                    result.Metadata["model"] = sdkResult.Metadata.Model;
                }
            }
            else
            {
                result.Error = sdkResult.Error;
                result.Summary = $"SDK execution failed: {sdkResult.Error}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing with SDK");
            result.Error = ex.Message;
            result.Summary = $"SDK execution error: {ex.Message}";
        }

        return result;
    }

    private async Task<WorkerTaskResult> ExecuteWithApiAsync(WorkerTask task, string voiceCommand)
    {
        _logger.LogInformation("=== EXECUTING WITH DIRECT API ===");
        
        var result = new WorkerTaskResult
        {
            TaskId = task.Id,
            Success = false
        };

        try
        {
            var workspaceRoot = Path.Combine(_options.WorkspaceBasePath, task.WorkspaceId);
            
            // Analyze repository context
            var repoContext = await _repositoryAnalyzer.AnalyzeRepositoryAsync(workspaceRoot);
            _logger.LogInformation("Repository analyzed - Type: {Type}, Files: {FileCount}", 
                repoContext.ProjectType, repoContext.RelevantFiles.Count);
            
            // Generate file operations using Claude API
            var fileOperations = await _claudeApi.GenerateFileOperationsAsync(voiceCommand, repoContext);
            _logger.LogInformation("Claude API returned {Count} operations", fileOperations.Operations.Count);
            
            // Execute file operations
            var executionResult = await _fileOperationExecutor.ExecuteOperationsAsync(
                fileOperations.Operations, 
                workspaceRoot);
            
            _logger.LogInformation("File operations completed: {Success}/{Total} successful",
                executionResult.SuccessfulOperations, executionResult.TotalOperations);
            
            // Build result
            var summaryBuilder = new StringBuilder();
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
                    ["executionMode"] = "API",
                    ["projectType"] = repoContext.ProjectType,
                    ["operationsRequested"] = fileOperations.Operations.Count,
                    ["operationsSucceeded"] = executionResult.SuccessfulOperations,
                    ["timestamp"] = DateTime.UtcNow
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing with API");
            result.Error = ex.Message;
            result.Summary = $"API execution error: {ex.Message}";
        }

        return result;
    }

    private string BuildSdkPrompt(string voiceCommand, RepositoryContext context)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine($"Voice command: {voiceCommand}");
        prompt.AppendLine($"\nProject type: {context.ProjectType}");
        prompt.AppendLine($"Working directory: {context.RootPath}");
        
        if (context.RelevantFiles.Any())
        {
            prompt.AppendLine("\nExisting files in the project:");
            foreach (var file in context.RelevantFiles.Take(10))
            {
                prompt.AppendLine($"- {file}");
            }
        }
        else
        {
            prompt.AppendLine("\nThe project directory is currently empty.");
        }
        
        prompt.AppendLine("\nIMPORTANT: Please CREATE the necessary files to implement the voice command.");
        prompt.AppendLine("- If asked to create a function, create a new file with that function");
        prompt.AppendLine("- If asked to create a program, create the appropriate source files");
        prompt.AppendLine("- Use appropriate file names and extensions based on the programming language");
        prompt.AppendLine("\nExecute the voice command by creating the requested code files.");
        
        return prompt.ToString();
    }

    private List<FileOperation> ParseSdkOutput(SidecarExecutionResult sdkResult, List<string> progressMessages)
    {
        var operations = new List<FileOperation>();
        
        // Since claude-agent writes files directly to the filesystem,
        // we don't need to track individual operations.
        // The files are already written to /workspaces/{workspaceId}/
        
        // We could parse tool calls if needed for audit/reporting:
        if (sdkResult.StreamUpdates != null)
        {
            foreach (var update in sdkResult.StreamUpdates.Where(u => u.Type == "tool_call"))
            {
                if (update.Data != null && 
                    update.Data.TryGetValue("tool", out var tool) && 
                    tool?.ToString() == "write_file")
                {
                    // Just for reporting - files are already written
                    operations.Add(new FileOperation
                    {
                        Type = update.Data.GetValueOrDefault("operationType")?.ToString() ?? "create",
                        FilePath = update.Data.GetValueOrDefault("filePath")?.ToString() ?? "unknown",
                        Content = update.Content
                    });
                }
            }
        }
        
        return operations;
    }

    private string GenerateSummary(SidecarExecutionResult sdkResult, List<FileOperation> operations)
    {
        var summary = new StringBuilder();
        summary.AppendLine("Claude Code SDK execution completed successfully.");
        
        if (operations.Any())
        {
            summary.AppendLine($"\nFile operations performed: {operations.Count}");
            foreach (var op in operations)
            {
                summary.AppendLine($"- {op.Type}: {op.FilePath}");
            }
        }
        
        if (sdkResult.ExecutionTime.TotalSeconds > 0)
        {
            summary.AppendLine($"\nExecution time: {sdkResult.ExecutionTime.TotalSeconds:F2} seconds");
        }
        
        if (!string.IsNullOrWhiteSpace(sdkResult.Output))
        {
            summary.AppendLine("\nSDK Output:");
            summary.AppendLine(sdkResult.Output);
        }
        
        return summary.ToString();
    }

}