using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using VoiceCode.WorkerService.Configuration;
using VoiceCode.WorkerService.Models;

namespace VoiceCode.WorkerService.Services;

public interface IMcpClientService : IDisposable
{
    Task<bool> ConnectAsync();
    Task DisconnectAsync();
    Task<List<Tool>> GetToolsAsync();
    Task<ToolCallResult> CallToolAsync(string toolName, Dictionary<string, object>? arguments);
    bool IsConnected { get; }
}

public class McpClientService : IMcpClientService
{
    private readonly ILogger<McpClientService> _logger;
    private readonly McpOptions _options;
    private Process? _serverProcess;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private readonly SemaphoreSlim _requestLock = new(1);
    private readonly Dictionary<string, TaskCompletionSource<McpResponse>> _pendingRequests = new();
    private CancellationTokenSource? _readCancellation;
    private Task? _readTask;
    
    public bool IsConnected { get; private set; }

    public McpClientService(ILogger<McpClientService> logger, IOptions<McpOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public async Task<bool> ConnectAsync()
    {
        try
        {
            _logger.LogInformation("Starting MCP server process...");
            
            var startInfo = new ProcessStartInfo
            {
                FileName = _options.ServerExecutable,
                Arguments = string.Join(" ", _options.ServerArguments),
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _serverProcess = Process.Start(startInfo);
            if (_serverProcess == null)
            {
                _logger.LogError("Failed to start MCP server process");
                return false;
            }

            _stdin = _serverProcess.StandardInput;
            _stdout = _serverProcess.StandardOutput;

            // Start reading responses
            _readCancellation = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadResponsesAsync(_readCancellation.Token));

            // Initialize connection
            var initParams = new InitializeParams
            {
                ProtocolVersion = "1.0",
                ClientInfo = new ClientInfo
                {
                    Name = "VoiceCode Worker Service",
                    Version = "1.0.0"
                },
                Capabilities = new ClientCapabilities
                {
                    Tools = new ToolsCapability { Call = true }
                }
            };

            var response = await SendRequestAsync("initialize", initParams);
            if (response?.Error != null)
            {
                _logger.LogError("Failed to initialize MCP connection: {Error}", response.Error.Message);
                return false;
            }

            IsConnected = true;
            _logger.LogInformation("MCP connection established successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to MCP server");
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            if (IsConnected)
            {
                await SendRequestAsync("shutdown", null);
                IsConnected = false;
            }

            _readCancellation?.Cancel();
            if (_readTask != null)
            {
                await _readTask;
            }

            _stdin?.Close();
            _stdout?.Close();
            
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                _serverProcess.Kill();
                await _serverProcess.WaitForExitAsync();
            }

            _serverProcess?.Dispose();
            _logger.LogInformation("MCP connection closed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting from MCP server");
        }
    }

    public async Task<List<Tool>> GetToolsAsync()
    {
        var response = await SendRequestAsync("tools/list", null);
        if (response?.Result != null)
        {
            var toolsJson = JsonConvert.SerializeObject(response.Result);
            var toolsResult = JsonConvert.DeserializeObject<Dictionary<string, List<Tool>>>(toolsJson);
            return toolsResult?["tools"] ?? new List<Tool>();
        }
        return new List<Tool>();
    }

    public async Task<ToolCallResult> CallToolAsync(string toolName, Dictionary<string, object>? arguments)
    {
        var toolCallParams = new ToolCallParams
        {
            Name = toolName,
            Arguments = arguments
        };

        var response = await SendRequestAsync("tools/call", toolCallParams);
        if (response?.Result != null)
        {
            var resultJson = JsonConvert.SerializeObject(response.Result);
            return JsonConvert.DeserializeObject<ToolCallResult>(resultJson) ?? new ToolCallResult { IsError = true };
        }

        return new ToolCallResult
        {
            IsError = true,
            Content = new List<ContentBlock>
            {
                new() { Type = "text", Text = response?.Error?.Message ?? "Unknown error" }
            }
        };
    }

    private async Task<McpResponse?> SendRequestAsync(string method, object? parameters)
    {
        var request = new McpRequest
        {
            Method = method,
            Params = parameters
        };

        var requestJson = JsonConvert.SerializeObject(request);
        var tcs = new TaskCompletionSource<McpResponse>();

        await _requestLock.WaitAsync();
        try
        {
            _pendingRequests[request.Id] = tcs;
            await _stdin!.WriteLineAsync(requestJson);
            await _stdin.FlushAsync();
        }
        finally
        {
            _requestLock.Release();
        }

        using var cts = new CancellationTokenSource(_options.RequestTimeoutSeconds * 1000);
        using (cts.Token.Register(() => tcs.TrySetCanceled()))
        {
            return await tcs.Task;
        }
    }

    private async Task ReadResponsesAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && !_stdout!.EndOfStream)
            {
                var line = await _stdout.ReadLineAsync();
                if (string.IsNullOrEmpty(line)) continue;

                try
                {
                    var response = JsonConvert.DeserializeObject<McpResponse>(line);
                    if (response?.Id != null && _pendingRequests.TryRemove(response.Id, out var tcs))
                    {
                        tcs.TrySetResult(response);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse MCP response: {Line}", line);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading MCP responses");
        }
    }

    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
        _requestLock?.Dispose();
        _readCancellation?.Dispose();
    }
}