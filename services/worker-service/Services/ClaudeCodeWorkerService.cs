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
    private readonly IMcpClientService _mcpClient;
    private readonly WorkerOptions _options;
    private readonly WorkerStatus _status;
    
    public ClaudeCodeWorkerService(
        ILogger<ClaudeCodeWorkerService> logger,
        IMcpClientService mcpClient,
        IOptions<WorkerOptions> options)
    {
        _logger = logger;
        _mcpClient = mcpClient;
        _options = options.Value;
        _status = new WorkerStatus
        {
            WorkerId = Guid.NewGuid().ToString(),
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

            // Ensure MCP connection
            if (!_mcpClient.IsConnected)
            {
                var connected = await _mcpClient.ConnectAsync();
                if (!connected)
                {
                    throw new InvalidOperationException("Failed to connect to MCP server");
                }
            }

            // Initialize workspace
            await InitializeWorkspaceAsync(task.WorkspaceId);

            // Execute based on task type
            switch (task.Type.ToLower())
            {
                case "code_generation":
                    result = await ExecuteCodeGenerationAsync(task);
                    break;
                case "refactoring":
                    result = await ExecuteRefactoringAsync(task);
                    break;
                case "testing":
                    result = await ExecuteTestingAsync(task);
                    break;
                case "file_operation":
                    result = await ExecuteFileOperationAsync(task);
                    break;
                case "command_execution":
                    result = await ExecuteCommandAsync(task);
                    break;
                default:
                    throw new NotSupportedException($"Task type '{task.Type}' is not supported");
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

    private async Task<WorkerTaskResult> ExecuteCodeGenerationAsync(WorkerTask task)
    {
        var result = new WorkerTaskResult { TaskId = task.Id };
        
        // Extract parameters
        var prompt = task.Parameters.GetValueOrDefault("prompt")?.ToString() ?? "";
        var language = task.Parameters.GetValueOrDefault("language")?.ToString() ?? "typescript";
        var filePath = task.Parameters.GetValueOrDefault("filePath")?.ToString();

        // Generate code using Claude through MCP
        var generateArgs = new Dictionary<string, object>
        {
            ["prompt"] = prompt,
            ["language"] = language
        };

        var toolResult = await _mcpClient.CallToolAsync("generate_code", generateArgs);
        
        if (!toolResult.IsError && toolResult.Content.Any())
        {
            var generatedCode = toolResult.Content.First().Text ?? "";
            
            // Write to file if path provided
            if (!string.IsNullOrEmpty(filePath))
            {
                var writeArgs = new Dictionary<string, object>
                {
                    ["path"] = filePath,
                    ["content"] = generatedCode
                };
                
                var writeResult = await _mcpClient.CallToolAsync("write_file", writeArgs);
                
                result.FileOperations.Add(new FileOperation
                {
                    Type = "create",
                    FilePath = filePath,
                    Content = generatedCode
                });
            }
            
            result.Success = true;
            result.Summary = $"Generated {language} code successfully";
        }
        else
        {
            result.Error = "Failed to generate code";
        }

        return result;
    }

    private async Task<WorkerTaskResult> ExecuteRefactoringAsync(WorkerTask task)
    {
        var result = new WorkerTaskResult { TaskId = task.Id };
        
        var filePath = task.Parameters.GetValueOrDefault("filePath")?.ToString() ?? "";
        var refactorType = task.Parameters.GetValueOrDefault("refactorType")?.ToString() ?? "";
        
        // Read current file
        var readArgs = new Dictionary<string, object> { ["path"] = filePath };
        var readResult = await _mcpClient.CallToolAsync("read_file", readArgs);
        
        if (!readResult.IsError && readResult.Content.Any())
        {
            var currentContent = readResult.Content.First().Text ?? "";
            
            // Perform refactoring
            var refactorArgs = new Dictionary<string, object>
            {
                ["code"] = currentContent,
                ["refactorType"] = refactorType
            };
            
            var refactorResult = await _mcpClient.CallToolAsync("refactor_code", refactorArgs);
            
            if (!refactorResult.IsError && refactorResult.Content.Any())
            {
                var refactoredCode = refactorResult.Content.First().Text ?? "";
                
                // Write back
                var writeArgs = new Dictionary<string, object>
                {
                    ["path"] = filePath,
                    ["content"] = refactoredCode
                };
                
                await _mcpClient.CallToolAsync("write_file", writeArgs);
                
                result.FileOperations.Add(new FileOperation
                {
                    Type = "edit",
                    FilePath = filePath,
                    Content = refactoredCode,
                    OldContent = currentContent
                });
                
                result.Success = true;
                result.Summary = $"Refactored {filePath} successfully";
            }
        }
        
        return result;
    }

    private async Task<WorkerTaskResult> ExecuteTestingAsync(WorkerTask task)
    {
        var result = new WorkerTaskResult { TaskId = task.Id };
        
        var testCommand = task.Parameters.GetValueOrDefault("command")?.ToString() ?? "npm test";
        var workingDir = task.Parameters.GetValueOrDefault("workingDirectory")?.ToString() ?? "/workspace";
        
        var commandArgs = new Dictionary<string, object>
        {
            ["command"] = testCommand,
            ["cwd"] = workingDir
        };
        
        var cmdResult = await _mcpClient.CallToolAsync("run_command", commandArgs);
        
        if (!cmdResult.IsError && cmdResult.Content.Any())
        {
            var output = cmdResult.Content.First().Text ?? "";
            
            result.CommandExecutions.Add(new CommandExecution
            {
                Command = testCommand,
                WorkingDirectory = workingDir,
                Output = output,
                ExitCode = 0
            });
            
            result.Success = true;
            result.Summary = "Tests executed successfully";
        }
        
        return result;
    }

    private async Task<WorkerTaskResult> ExecuteFileOperationAsync(WorkerTask task)
    {
        var result = new WorkerTaskResult { TaskId = task.Id };
        
        var operation = task.Parameters.GetValueOrDefault("operation")?.ToString() ?? "";
        var filePath = task.Parameters.GetValueOrDefault("path")?.ToString() ?? "";
        
        switch (operation.ToLower())
        {
            case "read":
                var readArgs = new Dictionary<string, object> { ["path"] = filePath };
                var readResult = await _mcpClient.CallToolAsync("read_file", readArgs);
                
                if (!readResult.IsError && readResult.Content.Any())
                {
                    result.FileOperations.Add(new FileOperation
                    {
                        Type = "read",
                        FilePath = filePath,
                        Content = readResult.Content.First().Text
                    });
                    result.Success = true;
                }
                break;
                
            case "write":
                var content = task.Parameters.GetValueOrDefault("content")?.ToString() ?? "";
                var writeArgs = new Dictionary<string, object>
                {
                    ["path"] = filePath,
                    ["content"] = content
                };
                
                var writeResult = await _mcpClient.CallToolAsync("write_file", writeArgs);
                result.Success = !writeResult.IsError;
                
                if (result.Success)
                {
                    result.FileOperations.Add(new FileOperation
                    {
                        Type = "create",
                        FilePath = filePath,
                        Content = content
                    });
                }
                break;
                
            case "search":
                var pattern = task.Parameters.GetValueOrDefault("pattern")?.ToString() ?? "";
                var searchArgs = new Dictionary<string, object>
                {
                    ["pattern"] = pattern,
                    ["path"] = filePath
                };
                
                var searchResult = await _mcpClient.CallToolAsync("search_files", searchArgs);
                result.Success = !searchResult.IsError;
                
                if (result.Success && searchResult.Content.Any())
                {
                    result.Summary = searchResult.Content.First().Text ?? "";
                }
                break;
        }
        
        return result;
    }

    private async Task<WorkerTaskResult> ExecuteCommandAsync(WorkerTask task)
    {
        var result = new WorkerTaskResult { TaskId = task.Id };
        
        var command = task.Parameters.GetValueOrDefault("command")?.ToString() ?? "";
        var workingDir = task.Parameters.GetValueOrDefault("workingDirectory")?.ToString() ?? "/workspace";
        
        var commandArgs = new Dictionary<string, object>
        {
            ["command"] = command,
            ["cwd"] = workingDir
        };
        
        var startTime = DateTime.UtcNow;
        var cmdResult = await _mcpClient.CallToolAsync("run_command", commandArgs);
        var duration = DateTime.UtcNow - startTime;
        
        if (!cmdResult.IsError && cmdResult.Content.Any())
        {
            var output = cmdResult.Content.First().Text ?? "";
            
            result.CommandExecutions.Add(new CommandExecution
            {
                Command = command,
                WorkingDirectory = workingDir,
                Output = output,
                ExitCode = 0,
                Duration = duration
            });
            
            result.Success = true;
            result.Summary = $"Command '{command}' executed successfully";
        }
        else
        {
            result.CommandExecutions.Add(new CommandExecution
            {
                Command = command,
                WorkingDirectory = workingDir,
                Error = cmdResult.Content.FirstOrDefault()?.Text ?? "Command failed",
                ExitCode = 1,
                Duration = duration
            });
        }
        
        return result;
    }

    public async Task<WorkerStatus> GetStatusAsync()
    {
        _status.LastHeartbeat = DateTime.UtcNow;
        return await Task.FromResult(_status);
    }

    public async Task<bool> InitializeWorkspaceAsync(string workspaceId)
    {
        try
        {
            var workspacePath = Path.Combine(_options.WorkspaceBasePath, workspaceId);
            
            // Create workspace directory
            var mkdirArgs = new Dictionary<string, object>
            {
                ["command"] = $"mkdir -p {workspacePath}",
                ["cwd"] = "/"
            };
            
            await _mcpClient.CallToolAsync("run_command", mkdirArgs);
            
            // Change to workspace directory
            var cdArgs = new Dictionary<string, object>
            {
                ["command"] = $"cd {workspacePath}",
                ["cwd"] = "/"
            };
            
            await _mcpClient.CallToolAsync("run_command", cdArgs);
            
            _logger.LogInformation("Initialized workspace {WorkspaceId} at {Path}", workspaceId, workspacePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize workspace {WorkspaceId}", workspaceId);
            return false;
        }
    }

    public async Task CleanupWorkspaceAsync(string workspaceId)
    {
        try
        {
            var workspacePath = Path.Combine(_options.WorkspaceBasePath, workspaceId);
            
            // Remove workspace directory
            var rmArgs = new Dictionary<string, object>
            {
                ["command"] = $"rm -rf {workspacePath}",
                ["cwd"] = "/"
            };
            
            await _mcpClient.CallToolAsync("run_command", rmArgs);
            
            _logger.LogInformation("Cleaned up workspace {WorkspaceId}", workspaceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup workspace {WorkspaceId}", workspaceId);
        }
    }
}