using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VoiceCode.WorkerService.Configuration;
using VoiceCode.WorkerService.Models;

namespace VoiceCode.WorkerService.Services;

public interface IClaudeCodeSidecarClient
{
    Task<bool> IsAvailableAsync();
    Task<SidecarExecutionResult> ExecuteAsync(string prompt, string? workingDirectory = null, string? sessionId = null);
    Task<SidecarExecutionResult> ExecuteWithStreamingAsync(
        string prompt, 
        Action<string> onProgress, 
        string? workingDirectory = null,
        string? sessionId = null,
        CancellationToken cancellationToken = default);
    Task<string> CreateSessionAsync(string workspaceId, Dictionary<string, object>? context = null);
    Task CloseSessionAsync(string sessionId);
}

public class ClaudeCodeSidecarClient : IClaudeCodeSidecarClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ClaudeCodeSidecarClient> _logger;
    private readonly WorkerOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;

    public ClaudeCodeSidecarClient(
        HttpClient httpClient,
        ILogger<ClaudeCodeSidecarClient> logger,
        IOptions<WorkerOptions> options)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
        
        // Configure base URL - sidecar runs on localhost:3000
        _httpClient.BaseAddress = new Uri(_options.SidecarUrl ?? "http://localhost:3000");
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/sdk/available");
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<AvailabilityResponse>(_jsonOptions);
                return result?.Available ?? false;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check sidecar availability");
            return false;
        }
    }

    public async Task<SidecarExecutionResult> ExecuteAsync(
        string prompt, 
        string? workingDirectory = null, 
        string? sessionId = null)
    {
        try
        {
            // Use the new /task endpoint for the Claude agent
            var request = new
            {
                message = prompt,
                sessionId = sessionId,
                workingDirectory = workingDirectory
            };

            var response = await _httpClient.PostAsJsonAsync("/task", request, _jsonOptions);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<TaskResponse>(_jsonOptions);
            
            return new SidecarExecutionResult
            {
                Success = result?.Success ?? false,
                Output = result?.Response ?? string.Empty,
                Error = result?.Error,
                ExecutionTime = TimeSpan.FromSeconds(1), // Default for now
                Metadata = result?.Usage != null ? new SidecarMetadata
                {
                    Model = "claude-3-opus-20240229",
                    TokensUsed = result.Usage.InputTokens + result.Usage.OutputTokens
                } : null,
                StreamUpdates = result?.ToolCalls?.Select(tc => new SidecarStreamUpdate
                {
                    Type = "tool_call",
                    Content = $"{tc.Tool}: {tc.Output?.Data ?? tc.Output?.Error}",
                    Timestamp = DateTime.UtcNow,
                    Data = new Dictionary<string, object>
                    {
                        ["tool"] = tc.Tool,
                        ["input"] = tc.Input,
                        ["output"] = tc.Output
                    }
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute command via sidecar");
            return new SidecarExecutionResult
            {
                Success = false,
                Error = ex.Message,
                ExecutionTime = TimeSpan.Zero
            };
        }
    }

    public async Task<SidecarExecutionResult> ExecuteWithStreamingAsync(
        string prompt,
        Action<string> onProgress,
        string? workingDirectory = null,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // For now, use the non-streaming endpoint
            // TODO: Implement SSE streaming when needed
            onProgress?.Invoke("Starting task execution...\n");
            
            var result = await ExecuteAsync(prompt, workingDirectory, sessionId);
            
            // Simulate streaming by sending tool calls as progress
            if (result.StreamUpdates != null)
            {
                foreach (var update in result.StreamUpdates)
                {
                    onProgress?.Invoke($"{update.Content}\n");
                }
            }
            
            onProgress?.Invoke("Task completed.\n");
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute streaming command via sidecar");
            return new SidecarExecutionResult
            {
                Success = false,
                Error = ex.Message,
                ExecutionTime = TimeSpan.Zero
            };
        }
    }

    public async Task<string> CreateSessionAsync(string workspaceId, Dictionary<string, object>? context = null)
    {
        try
        {
            var request = new
            {
                WorkspaceId = workspaceId,
                Context = context ?? new Dictionary<string, object>()
            };

            var response = await _httpClient.PostAsJsonAsync("/api/sdk/sessions", request, _jsonOptions);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<SessionCreationResponse>(_jsonOptions);
            return result?.SessionId ?? throw new InvalidOperationException("No session ID returned");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create session via sidecar");
            throw;
        }
    }

    public async Task CloseSessionAsync(string sessionId)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"/api/sdk/sessions/{sessionId}");
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to close session {SessionId}", sessionId);
        }
    }

    // Response DTOs
    private class AvailabilityResponse
    {
        public bool Available { get; set; }
        public string? Version { get; set; }
    }

    private class TaskResponse
    {
        public bool Success { get; set; }
        public string? Response { get; set; }
        public string? Error { get; set; }
        public List<ToolCall>? ToolCalls { get; set; }
        public UsageInfo? Usage { get; set; }
    }

    private class ToolCall
    {
        public string Tool { get; set; } = string.Empty;
        public object Input { get; set; } = new { };
        public ToolOutput Output { get; set; } = new();
    }

    private class ToolOutput
    {
        public bool Success { get; set; }
        public object? Data { get; set; }
        public string? Error { get; set; }
    }

    private class UsageInfo
    {
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
    }

    private class SdkExecutionRequest
    {
        public string Prompt { get; set; } = string.Empty;
        public string? WorkingDirectory { get; set; }
        public string? SessionId { get; set; }
        public ClaudeOptions? Options { get; set; }
    }

    private class ClaudeOptions
    {
        public string? Model { get; set; }
        public int? MaxTokens { get; set; }
        public double? Temperature { get; set; }
        public string? SystemPrompt { get; set; }
        public bool Stream { get; set; }
    }

    private class SdkExecutionResponse
    {
        public bool Success { get; set; }
        public string? Output { get; set; }
        public string? Error { get; set; }
        public string? SessionId { get; set; }
        public double? ExecutionTime { get; set; }
        public SidecarMetadata? Metadata { get; set; }
        public List<SidecarStreamUpdate>? SidecarStreamUpdates { get; set; }
    }

    private class SessionCreationResponse
    {
        public string SessionId { get; set; } = string.Empty;
    }

    private class StreamEventData
    {
        public string Type { get; set; } = string.Empty;
        public string? Content { get; set; }
        public string? Error { get; set; }
        public SidecarExecutionResult? Response { get; set; }
    }
}