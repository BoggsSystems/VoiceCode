using System.Text;
using VoiceCode.WorkerService.Models;

namespace VoiceCode.WorkerService.Services;

public interface IFileOperationExecutor
{
    Task<FileOperationResult> ExecuteOperationsAsync(
        List<ClaudeFileOperation> operations, 
        string workspaceRoot,
        CancellationToken cancellationToken = default);
}

public class FileOperationExecutor : IFileOperationExecutor
{
    private readonly ILogger<FileOperationExecutor> _logger;
    
    public FileOperationExecutor(ILogger<FileOperationExecutor> logger)
    {
        _logger = logger;
    }
    
    public async Task<FileOperationResult> ExecuteOperationsAsync(
        List<ClaudeFileOperation> operations, 
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        var result = new FileOperationResult
        {
            TotalOperations = operations.Count
        };
        
        _logger.LogInformation("Executing {Count} file operations in workspace: {Workspace}", 
            operations.Count, workspaceRoot);
        
        foreach (var operation in operations)
        {
            try
            {
                _logger.LogInformation("Executing {Type} operation on {Path}", 
                    operation.Type, operation.Path);
                
                var executedOp = await ExecuteOperationAsync(operation, workspaceRoot, cancellationToken);
                result.ExecutedOperations.Add(executedOp);
                
                if (executedOp.Success)
                {
                    result.SuccessfulOperations++;
                }
                else
                {
                    result.FailedOperations++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute {Type} operation on {Path}", 
                    operation.Type, operation.Path);
                
                result.ExecutedOperations.Add(new ExecutedFileOperation
                {
                    Operation = operation,
                    Success = false,
                    Error = ex.Message
                });
                result.FailedOperations++;
            }
        }
        
        _logger.LogInformation("File operations completed: {Success}/{Total} successful", 
            result.SuccessfulOperations, result.TotalOperations);
        
        return result;
    }
    
    private async Task<ExecutedFileOperation> ExecuteOperationAsync(
        ClaudeFileOperation operation, 
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.Combine(workspaceRoot, operation.Path);
        var executedOp = new ExecutedFileOperation
        {
            Operation = operation,
            FullPath = fullPath
        };
        
        switch (operation.Type.ToLowerInvariant())
        {
            case "create":
                await ExecuteCreateAsync(fullPath, operation.Content ?? "", executedOp);
                break;
                
            case "edit":
                await ExecuteEditAsync(fullPath, operation.OldContent, operation.NewContent, executedOp);
                break;
                
            case "delete":
                await ExecuteDeleteAsync(fullPath, executedOp);
                break;
                
            case "read":
                await ExecuteReadAsync(fullPath, executedOp);
                break;
                
            default:
                executedOp.Success = false;
                executedOp.Error = $"Unknown operation type: {operation.Type}";
                break;
        }
        
        return executedOp;
    }
    
    private async Task ExecuteCreateAsync(string fullPath, string content, ExecutedFileOperation result)
    {
        try
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _logger.LogInformation("Created directory: {Directory}", directory);
            }
            
            // Check if file already exists
            if (File.Exists(fullPath))
            {
                result.Success = false;
                result.Error = "File already exists";
                _logger.LogWarning("Cannot create file {Path} - already exists", fullPath);
                return;
            }
            
            // Write file
            await File.WriteAllTextAsync(fullPath, content);
            result.Success = true;
            result.ResultContent = content;
            _logger.LogInformation("Created file: {Path} ({Length} bytes)", fullPath, content.Length);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            throw;
        }
    }
    
    private async Task ExecuteEditAsync(string fullPath, string? oldContent, string? newContent, ExecutedFileOperation result)
    {
        try
        {
            if (!File.Exists(fullPath))
            {
                result.Success = false;
                result.Error = "File does not exist";
                _logger.LogWarning("Cannot edit file {Path} - does not exist", fullPath);
                return;
            }
            
            var currentContent = await File.ReadAllTextAsync(fullPath);
            result.OriginalContent = currentContent;
            
            if (string.IsNullOrEmpty(oldContent) || string.IsNullOrEmpty(newContent))
            {
                result.Success = false;
                result.Error = "Both oldContent and newContent are required for edit operations";
                return;
            }
            
            // Perform replacement
            if (!currentContent.Contains(oldContent))
            {
                result.Success = false;
                result.Error = "Old content not found in file";
                _logger.LogWarning("Old content not found in file {Path}", fullPath);
                return;
            }
            
            var updatedContent = currentContent.Replace(oldContent, newContent);
            await File.WriteAllTextAsync(fullPath, updatedContent);
            
            result.Success = true;
            result.ResultContent = updatedContent;
            _logger.LogInformation("Edited file: {Path} (replaced {OldLength} chars with {NewLength} chars)", 
                fullPath, oldContent.Length, newContent.Length);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            throw;
        }
    }
    
    private async Task ExecuteDeleteAsync(string fullPath, ExecutedFileOperation result)
    {
        try
        {
            if (!File.Exists(fullPath))
            {
                result.Success = false;
                result.Error = "File does not exist";
                _logger.LogWarning("Cannot delete file {Path} - does not exist", fullPath);
                return;
            }
            
            // Store content before deletion
            result.OriginalContent = await File.ReadAllTextAsync(fullPath);
            
            File.Delete(fullPath);
            result.Success = true;
            _logger.LogInformation("Deleted file: {Path}", fullPath);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            throw;
        }
    }
    
    private async Task ExecuteReadAsync(string fullPath, ExecutedFileOperation result)
    {
        try
        {
            if (!File.Exists(fullPath))
            {
                result.Success = false;
                result.Error = "File does not exist";
                _logger.LogWarning("Cannot read file {Path} - does not exist", fullPath);
                return;
            }
            
            var content = await File.ReadAllTextAsync(fullPath);
            result.Success = true;
            result.ResultContent = content;
            _logger.LogInformation("Read file: {Path} ({Length} bytes)", fullPath, content.Length);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            throw;
        }
    }
}

public class FileOperationResult
{
    public int TotalOperations { get; set; }
    public int SuccessfulOperations { get; set; }
    public int FailedOperations { get; set; }
    public List<ExecutedFileOperation> ExecutedOperations { get; set; } = new();
}

public class ExecutedFileOperation
{
    public ClaudeFileOperation Operation { get; set; } = new();
    public string FullPath { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? OriginalContent { get; set; }
    public string? ResultContent { get; set; }
}