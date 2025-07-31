using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoiceCode.RouterService.Services;
using VoiceCode.Common.Models;
using VoiceCode.Common.Interfaces;
using System.Net.Http.Json;
using System.Text.Json;

namespace VoiceCode.RouterService.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class VoiceCommandController : ControllerBase
{
    private readonly ILogger<VoiceCommandController> _logger;
    private readonly IIntentClassifier _intentClassifier;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IAudioResponseTableService _audioResponseTable;

    public VoiceCommandController(
        ILogger<VoiceCommandController> logger,
        IIntentClassifier intentClassifier,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IAudioResponseTableService audioResponseTable)
    {
        _logger = logger;
        _intentClassifier = intentClassifier;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _audioResponseTable = audioResponseTable;
    }

    [HttpPost("process")]
    public async Task<IActionResult> ProcessVoiceCommand([FromBody] VoiceCommandRequest request)
    {
        try
        {
            _logger.LogInformation("Processing voice command: {Transcription} with SessionId: {SessionId}", 
                request.Transcription, request.SessionId ?? "none");

            // Step 1: Classify intent using OpenAI
            var classification = await _intentClassifier.ClassifyAsync(request.Transcription, new Dictionary<string, object>
            {
                ["user_id"] = User.Identity?.Name ?? "unknown",
                ["session_id"] = request.SessionId ?? Guid.NewGuid().ToString(),
                ["timestamp"] = DateTime.UtcNow
            });

            _logger.LogInformation("Command classified for Worker {Worker} with instructions: {Instructions}", 
                classification.Worker, classification.Instructions);

            // Check if this is an ideation/design command
            var ideationKeywords = new[] { "thinking about", "idea", "what if", "how about", "design", "build a", "create a", "platform", "application", "system" };
            var isIdeationCommand = ideationKeywords.Any(keyword => request.Transcription.ToLower().Contains(keyword));

            var httpClient = _httpClientFactory.CreateClient();

            if (isIdeationCommand)
            {
                _logger.LogInformation("Detected ideation command, routing to orchestrator");
                
                // Forward to orchestrator instead of dispatcher
                var orchestratorUrl = _configuration["ServiceEndpoints:Orchestrator"] ?? 
                                     "https://voicecode-orchestrator.orangewater-a2f689a8.eastus.azurecontainerapps.io";
                
                var orchestratorRequest = new
                {
                    UserId = User.Identity?.Name ?? "default-user",
                    TranscribedText = request.Transcription,
                    AudioUrl = (string?)null, // VoiceCommandRequest doesn't have AudioUrl
                    Metadata = new Dictionary<string, object>
                    {
                        ["sessionId"] = request.SessionId ?? Guid.NewGuid().ToString(),
                        ["source"] = "router-service"
                    }
                };
                
                var orchestratorResponse = await httpClient.PostAsJsonAsync(
                    $"{orchestratorUrl}/api/v2/orchestrate/voice-command", 
                    orchestratorRequest);
                    
                if (orchestratorResponse.IsSuccessStatusCode)
                {
                    var resultContent = await orchestratorResponse.Content.ReadAsStringAsync();
                    var orchestratorResult = JsonSerializer.Deserialize<JsonElement>(resultContent);
                    
                    // Store audio response if available
                    var audioUrl = orchestratorResult.TryGetProperty("audioUrl", out var audioUrlProp) ? audioUrlProp.GetString() : null;
                    if (!string.IsNullOrEmpty(audioUrl) && !string.IsNullOrEmpty(request.SessionId))
                    {
                        // Store audio response - need to check the method signature
                        // await _audioResponseTable.StoreAudioResponseAsync(request.SessionId, audioUrl);
                    }
                    
                    // Return the orchestrator response
                    return Ok(new VoiceCommandResponse
                    {
                        TaskId = orchestratorResult.TryGetProperty("sessionId", out var sessionIdProp) ? sessionIdProp.GetString() : Guid.NewGuid().ToString(),
                        Success = orchestratorResult.TryGetProperty("success", out var successProp) && successProp.GetBoolean(),
                        Worker = 0, // Orchestrator doesn't use worker numbers
                        Instructions = "Processing ideation request",
                        Response = orchestratorResult.TryGetProperty("voiceSummary", out var summaryProp) ? summaryProp.GetString() : 
                                  orchestratorResult.TryGetProperty("message", out var msgProp) ? msgProp.GetString() : "Processing your request",
                        Metadata = new Dictionary<string, object>
                        {
                            ["audioUrl"] = audioUrl ?? "",
                            ["phase"] = orchestratorResult.TryGetProperty("phase", out var phaseProp) ? phaseProp.GetString() : "Processing",
                            ["workerId"] = orchestratorResult.TryGetProperty("workerId", out var workerProp) ? workerProp.GetString() : "orchestrator"
                        }
                    });
                }
                else
                {
                    var error = await orchestratorResponse.Content.ReadAsStringAsync();
                    _logger.LogError("Orchestrator returned error: {StatusCode} - {Error}", 
                        orchestratorResponse.StatusCode, error);
                    // Fall through to dispatcher
                }
            }

            // Step 2: Forward to Dispatcher for execution (for non-ideation commands)
            var dispatcherUrl = _configuration["ServiceEndpoints:Dispatcher"] ?? 
                               "https://voicecode-dispatcher.orangewater-a2f689a8.eastus.azurecontainerapps.io";
            
            // Create the request message with proper headers
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, 
                $"{dispatcherUrl}/api/dispatcher/execute-task");
            
            // Forward auth header
            if (Request.Headers.ContainsKey("Authorization"))
            {
                var authHeader = Request.Headers["Authorization"].ToString();
                _logger.LogInformation("Forwarding auth header to Dispatcher: {AuthHeaderPrefix}", 
                    authHeader.Length > 20 ? authHeader.Substring(0, 20) + "..." : authHeader);
                requestMessage.Headers.Add("Authorization", authHeader);
            }
            else
            {
                _logger.LogWarning("No Authorization header found in request to forward to Dispatcher");
            }

            var dispatcherRequest = new
            {
                TaskId = Guid.NewGuid().ToString(),
                Worker = classification.Worker,
                Instructions = classification.Instructions,
                Context = new
                {
                    UserId = User.Identity?.Name ?? "unknown",
                    SessionId = request.SessionId,
                    Timestamp = DateTime.UtcNow,
                    OriginalTranscription = request.Transcription
                }
            };

            requestMessage.Content = JsonContent.Create(dispatcherRequest);
            
            var response = await httpClient.SendAsync(requestMessage);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("Dispatcher returned error: {StatusCode} - {Error}", 
                    response.StatusCode, error);
                return StatusCode((int)response.StatusCode, new { error = "Failed to execute task", details = error });
            }

            var result = await response.Content.ReadFromJsonAsync<TaskExecutionResponse>();
            
            return Ok(new VoiceCommandResponse
            {
                Success = true,
                TaskId = dispatcherRequest.TaskId,
                Worker = classification.Worker,
                Instructions = classification.Instructions,
                Response = result?.Response ?? "Task is being processed",
                Metadata = new Dictionary<string, object>
                {
                    ["processing_time_ms"] = (DateTime.UtcNow - request.Timestamp).TotalMilliseconds
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing voice command");
            return StatusCode(500, new { error = "Internal server error", message = ex.Message });
        }
    }

    [HttpGet("{taskId}/audio")]
    public async Task<IActionResult> GetAudioResponse(string taskId)
    {
        try
        {
            _logger.LogInformation("Checking audio response for task {TaskId}", taskId);

            var audioResponse = await _audioResponseTable.GetAudioResponseAsync(taskId);
            
            if (audioResponse == null)
            {
                _logger.LogDebug("No audio response available yet for task {TaskId}", taskId);
                return Ok(new AudioPollResponse
                {
                    TaskId = taskId,
                    Status = "pending",
                    Message = "Audio response not ready yet"
                });
            }

            _logger.LogInformation("Audio response found for task {TaskId}, URL: {AudioUrl}", 
                taskId, audioResponse.AudioUrl);

            return Ok(new AudioPollResponse
            {
                TaskId = taskId,
                Status = "ready",
                AudioUrl = audioResponse.AudioUrl,
                Text = audioResponse.Text,
                Duration = audioResponse.Duration,
                Timestamp = audioResponse.CreatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving audio response for task {TaskId}", taskId);
            return StatusCode(500, new { error = "Failed to retrieve audio response" });
        }
    }
}

public class VoiceCommandRequest
{
    public string Transcription { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object>? Metadata { get; set; }
}

public class VoiceCommandResponse
{
    public bool Success { get; set; }
    public string TaskId { get; set; } = string.Empty;
    public int Worker { get; set; }
    public string Instructions { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public Dictionary<string, object>? Metadata { get; set; }
}

// Response from Dispatcher
public class TaskExecutionResponse
{
    public string TaskId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public int? WorkerNumber { get; set; }
    public string? Error { get; set; }
}

// Audio polling response
public class AudioPollResponse
{
    public string TaskId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // "pending" or "ready"
    public string? Message { get; set; }
    public string? AudioUrl { get; set; }
    public string? Text { get; set; }
    public double? Duration { get; set; }
    public DateTime? Timestamp { get; set; }
}