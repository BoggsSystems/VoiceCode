using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;
using VoiceCode.Common.Interfaces;
using VoiceCode.Common.Models;

namespace VoiceCode.STTService.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
[RequiredScope(RequiredScopesConfigurationKey = "AzureAd:Scopes")]
public class TranscriptionController : ControllerBase
{
    private readonly ISTTService _sttService;
    private readonly IAudioStorageService _audioStorage;
    private readonly ICacheService _cache;
    private readonly ILogger<TranscriptionController> _logger;

    public TranscriptionController(
        ISTTService sttService,
        IAudioStorageService audioStorage,
        ICacheService cache,
        ILogger<TranscriptionController> logger)
    {
        _sttService = sttService;
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
            var result = await _sttService.TranscribeAsync(audioData, request.Language ?? "en-US");
            return Ok(result);
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
}

public class TranscribeStreamRequest
{
    public string AudioData { get; set; } = string.Empty;
    public string? Language { get; set; }
}

public class TranscriptionStatus
{
    public string RequestId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}