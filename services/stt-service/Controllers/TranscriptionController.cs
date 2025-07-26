using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;
using VoiceCode.Common.Interfaces;
using VoiceCode.Common.Models;
using VoiceCode.STTService.Services;

namespace VoiceCode.STTService.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class TranscriptionController : ControllerBase
{
    private readonly ISTTService _sttService;
    private readonly IStreamingSTTService _streamingSTTService;
    private readonly IEnhancedStreamingSTTService _enhancedStreamingService;
    private readonly IAudioStorageService _audioStorage;
    private readonly ICacheService _cache;
    private readonly ILogger<TranscriptionController> _logger;

    public TranscriptionController(
        ISTTService sttService,
        IStreamingSTTService streamingSTTService,
        IEnhancedStreamingSTTService enhancedStreamingService,
        IAudioStorageService audioStorage,
        ICacheService cache,
        ILogger<TranscriptionController> logger)
    {
        _sttService = sttService;
        _streamingSTTService = streamingSTTService;
        _enhancedStreamingService = enhancedStreamingService;
        _audioStorage = audioStorage;
        _cache = cache;
        _logger = logger;
    }

    [HttpPost("transcribe")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10MB limit
    public async Task<ActionResult<TranscriptionResult>> TranscribeAudio(
        [FromForm] IFormFile audioFile,
        [FromForm] string? language = "en-US")
    {
        if (audioFile == null || audioFile.Length == 0)
        {
            return BadRequest(new { error = "No audio file provided" });
        }

        var requestId = Guid.NewGuid().ToString();
        using var activity = System.Diagnostics.Activity.Current;
        activity?.SetTag("request.id", requestId);
        activity?.SetTag("audio.size", audioFile.Length);
        activity?.SetTag("audio.type", audioFile.ContentType);

        try
        {
            _logger.LogInformation("Processing transcription request {RequestId} for {FileName} ({Size} bytes)",
                requestId, audioFile.FileName, audioFile.Length);

            // Read audio data
            using var memoryStream = new MemoryStream();
            await audioFile.CopyToAsync(memoryStream);
            var audioData = memoryStream.ToArray();

            // Store audio for audit/replay
            var audioUrl = await _audioStorage.StoreAudioAsync(
                audioData, 
                requestId, 
                audioFile.ContentType);

            // Check cache first
            var cacheKey = $"transcription:{Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(audioData))}:{language}";
            var cachedResult = await _cache.GetAsync<TranscriptionResult>(cacheKey);
            if (cachedResult != null)
            {
                _logger.LogInformation("Returning cached transcription for request {RequestId}", requestId);
                cachedResult.Id = requestId; // Update ID for this request
                return Ok(cachedResult);
            }

            // Perform transcription
            var result = await _sttService.TranscribeAsync(audioData, language);
            result.AudioUrl = audioUrl;
            result.RequestedAt = DateTime.UtcNow;
            result.UserId = User.Identity?.Name ?? "anonymous";

            // Cache the result
            await _cache.SetAsync(cacheKey, result, TimeSpan.FromHours(1));

            _logger.LogInformation("Transcription completed for request {RequestId}: {WordCount} words, {Confidence:P} confidence",
                requestId, result.Words?.Count ?? 0, result.Confidence);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Invalid operation during transcription for request {RequestId}", requestId);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transcription failed for request {RequestId}", requestId);
            return StatusCode(500, new { error = "Transcription failed", requestId });
        }
    }

    [HttpPost("transcribe-stream")]
    public async Task<ActionResult<TranscriptionResult>> TranscribeStream(
        [FromBody] TranscribeStreamRequest request)
    {
        if (request.AudioData == null || request.AudioData.Length == 0)
        {
            return BadRequest(new { error = "No audio data provided" });
        }

        try
        {
            var audioData = Convert.FromBase64String(request.AudioData);
            
            // For streaming requests, handle them differently
            if (request.IsStreaming)
            {
                // Ensure session exists
                if (string.IsNullOrEmpty(request.SessionId))
                {
                    request.SessionId = Guid.NewGuid().ToString();
                    await _streamingSTTService.StartStreamingSessionAsync(request.SessionId, request.Language ?? "en-US");
                }
                
                // Process audio chunk
                var result = await _streamingSTTService.ProcessAudioChunkAsync(request.SessionId, audioData);
                return Ok(result);
            }
            else
            {
                // Non-streaming request - process normally
                var result = await _sttService.TranscribeAsync(audioData, request.Language ?? "en-US");
                return Ok(result);
            }
        }
        catch (FormatException)
        {
            return BadRequest(new { error = "Invalid base64 audio data" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stream transcription failed");
            return StatusCode(500, new { error = "Transcription failed" });
        }
    }

    [HttpGet("languages")]
    public async Task<ActionResult<List<string>>> GetSupportedLanguages()
    {
        var languages = await _sttService.GetSupportedLanguagesAsync();
        return Ok(languages);
    }

    [HttpGet("status/{requestId}")]
    public async Task<ActionResult<TranscriptionStatus>> GetTranscriptionStatus(string requestId)
    {
        // This endpoint would be used for async/long-running transcriptions
        // For now, we'll return a simple completed status
        return Ok(new TranscriptionStatus
        {
            RequestId = requestId,
            Status = "completed",
            Message = "Transcription completed"
        });
    }

    [HttpPost("stream/start")]
    public async Task<ActionResult<StreamSessionResponse>> StartStreamSession(
        [FromBody] StartStreamRequest request)
    {
        try
        {
            var sessionId = request.SessionId ?? Guid.NewGuid().ToString();
            
            // Use enhanced streaming service if enhanced features requested
            if (request.UseEnhanced)
            {
                await _enhancedStreamingService.StartStreamingSessionAsync(sessionId, request.Language ?? "en-US");
                
                return Ok(new StreamSessionResponse
                {
                    SessionId = sessionId,
                    Status = "active",
                    Enhanced = true,
                    Message = "Enhanced streaming session started"
                });
            }
            else
            {
                await _streamingSTTService.StartStreamingSessionAsync(sessionId, request.Language ?? "en-US");
                
                return Ok(new StreamSessionResponse
                {
                    SessionId = sessionId,
                    Status = "active",
                    Enhanced = false,
                    Message = "Streaming session started"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start streaming session");
            return BadRequest(new { error = "Failed to start streaming session", details = ex.Message });
        }
    }

    [HttpGet("stream/{sessionId}/info")]
    public async Task<ActionResult<StreamingSessionInfo>> GetStreamInfo(string sessionId)
    {
        var info = await _enhancedStreamingService.GetSessionInfoAsync(sessionId);
        
        if (info == null)
        {
            return NotFound(new { error = "Session not found" });
        }
        
        return Ok(info);
    }

    [HttpGet("stream/{sessionId}/transcripts")]
    public async Task<ActionResult<List<TranscriptionSegment>>> GetSessionTranscripts(string sessionId)
    {
        var transcripts = await _enhancedStreamingService.GetSessionTranscriptsAsync(sessionId);
        return Ok(transcripts);
    }

    [HttpGet("stream/{sessionId}/metrics")]
    public async Task<ActionResult<StreamingMetrics>> GetSessionMetrics(string sessionId)
    {
        var metrics = await _enhancedStreamingService.GetSessionMetricsAsync(sessionId);
        
        if (metrics == null)
        {
            return NotFound(new { error = "Session not found" });
        }
        
        return Ok(metrics);
    }

    [HttpPost("stream/{sessionId}/end")]
    public async Task<ActionResult<TranscriptionResult>> EndStreamSession(string sessionId)
    {
        try
        {
            var result = await _enhancedStreamingService.EndStreamingSessionAsync(sessionId);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to end streaming session");
            return StatusCode(500, new { error = "Failed to end streaming session" });
        }
    }
}

public class TranscribeStreamRequest
{
    public string AudioData { get; set; } = string.Empty;
    public string? Language { get; set; }
    public string? SessionId { get; set; }
    public bool IsStreaming { get; set; }
}

public class TranscriptionStatus
{
    public string RequestId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class StartStreamRequest
{
    public string? SessionId { get; set; }
    public string? Language { get; set; }
    public bool UseEnhanced { get; set; }
}

public class StreamSessionResponse
{
    public string SessionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool Enhanced { get; set; }
    public string Message { get; set; } = string.Empty;
}