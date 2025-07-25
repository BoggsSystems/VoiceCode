using System.Diagnostics;
using System.Text;

namespace VoiceCode.WorkerService.Services;

public interface IClaudeCodeCliService
{
    Task<ClaudeCodeResult> ExecuteCommandAsync(string voiceCommand, string workspaceId, CancellationToken cancellationToken = default);
}

public class ClaudeCodeCliService : IClaudeCodeCliService
{
    private readonly ILogger<ClaudeCodeCliService> _logger;
    private readonly string _claudeExecutable;
    
    public ClaudeCodeCliService(ILogger<ClaudeCodeCliService> logger)
    {
        _logger = logger;
        _claudeExecutable = Environment.GetEnvironmentVariable("CLAUDE_EXECUTABLE") ?? "claude";
    }
    
    public async Task<ClaudeCodeResult> ExecuteCommandAsync(string voiceCommand, string workspaceId, CancellationToken cancellationToken = default)
    {
        // Use the root workspace where the repository is cloned
        var workspaceDir = "/workspace";
        
        // Ensure workspace directory exists
        if (!Directory.Exists(workspaceDir))
        {
            _logger.LogWarning("Workspace directory {WorkspaceDir} does not exist", workspaceDir);
            Directory.CreateDirectory(workspaceDir);
        }
        
        _logger.LogInformation("Executing Claude Code CLI for voice command: {VoiceCommand}", voiceCommand);
        
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _claudeExecutable,
                    Arguments = "", // Let Claude interpret the command
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = workspaceDir,
                    Environment =
                    {
                        ["ANTHROPIC_API_KEY"] = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "",
                        ["GITHUB_TOKEN"] = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? ""
                    }
                }
            };
            
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();
            
            // Capture output asynchronously
            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    outputBuilder.AppendLine(e.Data);
                    _logger.LogDebug("Claude output: {Output}", e.Data);
                }
            };
            
            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorBuilder.AppendLine(e.Data);
                    _logger.LogWarning("Claude error: {Error}", e.Data);
                }
            };
            
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            
            // Send the voice command
            await process.StandardInput.WriteLineAsync(voiceCommand);
            process.StandardInput.Close();
            
            // Wait for completion with timeout
            var completed = await WaitForExitAsync(process, TimeSpan.FromMinutes(5), cancellationToken);
            
            if (!completed)
            {
                process.Kill();
                return new ClaudeCodeResult
                {
                    Success = false,
                    Output = outputBuilder.ToString(),
                    Error = "Process timed out after 5 minutes",
                    ExitCode = -1
                };
            }
            
            return new ClaudeCodeResult
            {
                Success = process.ExitCode == 0,
                Output = outputBuilder.ToString(),
                Error = errorBuilder.ToString(),
                ExitCode = process.ExitCode
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute Claude Code CLI");
            return new ClaudeCodeResult
            {
                Success = false,
                Output = "",
                Error = $"Exception: {ex.Message}",
                ExitCode = -1
            };
        }
    }
    
    private async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>();
        
        process.EnableRaisingEvents = true;
        process.Exited += (sender, args) => tcs.TrySetResult(true);
        
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        
        using (cts.Token.Register(() => tcs.TrySetResult(false)))
        {
            return await tcs.Task;
        }
    }
}

public class ClaudeCodeResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public int ExitCode { get; set; }
}