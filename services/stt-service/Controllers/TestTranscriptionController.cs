using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoiceCode.Common.Interfaces;
using VoiceCode.Common.Models;

namespace VoiceCode.STTService.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/test/[controller]")]
public class TestTranscriptionController : ControllerBase
{
    private readonly ISTTService _sttService;
    private readonly ILogger<TestTranscriptionController> _logger;

    public TestTranscriptionController(
        ISTTService sttService,
        ILogger<TestTranscriptionController> logger)
    {
        _sttService = sttService;
        _logger = logger;
    }

    [HttpPost("transcribe")]
    [AllowAnonymous]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10MB limit
    public async Task<ActionResult<TranscriptionResult>> TranscribeAudio(
        [FromForm] IFormFile audioFile,
        [FromForm] string? language = "en-US")
    {
        _logger.LogInformation("Test transcription endpoint called - no auth required");
        
        if (audioFile == null || audioFile.Length == 0)
        {
            return BadRequest(new { error = "No audio file provided" });
        }

        var requestId = Guid.NewGuid().ToString();
        
        try
        {
            _logger.LogInformation("Processing test transcription request {RequestId} for {FileName} ({Size} bytes)",
                requestId, audioFile.FileName, audioFile.Length);

            // Read audio data
            using var memoryStream = new MemoryStream();
            await audioFile.CopyToAsync(memoryStream);
            var audioData = memoryStream.ToArray();

            // Perform transcription
            var result = await _sttService.TranscribeAsync(audioData, language);
            result.RequestedAt = DateTime.UtcNow;
            result.UserId = "test-user";

            _logger.LogInformation("Test transcription completed for request {RequestId}: {WordCount} words, {Confidence:P} confidence",
                requestId, result.Words?.Count ?? 0, result.Confidence);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test transcription failed for request {RequestId}", requestId);
            return StatusCode(500, new { error = "Transcription failed", requestId });
        }
    }

    [HttpGet("health")]
    [AllowAnonymous]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", service = "stt-test", timestamp = DateTime.UtcNow });
    }
}