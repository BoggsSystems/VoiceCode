using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoiceCode.RouterService.Services;
using VoiceCode.Common.Models;

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

    public VoiceCommandController(
        ILogger<VoiceCommandController> logger,
        IIntentClassifier intentClassifier,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _logger = logger;
        _intentClassifier = intentClassifier;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    [HttpPost("process")]
    public async Task<IActionResult> ProcessVoiceCommand([FromBody] VoiceCommandRequest request)
    {
        try
        {
            _logger.LogInformation("Processing voice command: {Transcription}", request.Transcription);

            // Step 1: Classify intent using OpenAI
            var classification = await _intentClassifier.ClassifyAsync(request.Transcription, new Dictionary<string, object>
            {
                ["user_id"] = User.Identity?.Name ?? "unknown",
                ["session_id"] = request.SessionId ?? Guid.NewGuid().ToString(),
                ["timestamp"] = DateTime.UtcNow
            });

            _logger.LogInformation("Intent classified: {Intent} with confidence {Confidence}", 
                classification.Intent, classification.Confidence);

            // Step 2: Forward to Dispatcher for execution
            var dispatcherUrl = _configuration["ServiceEndpoints:Dispatcher"] ?? 
                               "https://voicecode-dispatcher.orangewater-a2f689a8.eastus.azurecontainerapps.io";
            
            var httpClient = _httpClientFactory.CreateClient();
            
            // Forward auth header
            if (Request.Headers.ContainsKey("Authorization"))
            {
                httpClient.DefaultRequestHeaders.Add("Authorization", Request.Headers["Authorization"].ToString());
            }

            var dispatcherRequest = new
            {
                TaskId = Guid.NewGuid().ToString(),
                OriginalRequest = request.Transcription,
                Intent = classification.Intent,
                Confidence = classification.Confidence,
                Metadata = classification.Metadata,
                Context = new
                {
                    UserId = User.Identity?.Name ?? "unknown",
                    SessionId = request.SessionId,
                    Timestamp = DateTime.UtcNow,
                    AudioMetadata = request.Metadata
                }
            };

            var response = await httpClient.PostAsJsonAsync(
                $"{dispatcherUrl}/api/dispatcher/execute-task", 
                dispatcherRequest);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("Dispatcher returned error: {StatusCode} - {Error}", 
                    response.StatusCode, error);
                return StatusCode((int)response.StatusCode, new { error = "Failed to execute task", details = error });
            }

            var result = await response.Content.ReadFromJsonAsync<dynamic>();
            
            return Ok(new VoiceCommandResponse
            {
                Success = true,
                TaskId = dispatcherRequest.TaskId,
                Intent = classification.Intent,
                Response = result?.response ?? "Task is being processed",
                Metadata = new Dictionary<string, object>
                {
                    ["confidence"] = classification.Confidence,
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
    public string Intent { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public Dictionary<string, object>? Metadata { get; set; }
}