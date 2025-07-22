using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;
using VoiceCode.Common.Models;
using VoiceCode.TTSService.Services.Interfaces;

namespace VoiceCode.TTSService.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
[RequiredScope(RequiredScopesConfigurationKey = "AzureAd:Scopes")]
public class SpeechController : ControllerBase
{
    private readonly ILogger<SpeechController> _logger;
    private readonly ITTSService _ttsService;
    private readonly IAudioStorageService _audioStorage;
    private readonly IVoicePersonalityService _personalityService;

    public SpeechController(
        ILogger<SpeechController> logger,
        ITTSService ttsService,
        IAudioStorageService audioStorage,
        IVoicePersonalityService personalityService)
    {
        _logger = logger;
        _ttsService = ttsService;
        _audioStorage = audioStorage;
        _personalityService = personalityService;
    }

    [HttpPost("synthesize")]
    public async Task<ActionResult<SynthesisResult>> Synthesize([FromBody] SynthesisRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return BadRequest("Text is required");
            }

            var result = await _ttsService.SynthesizeAsync(request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error synthesizing speech");
            return StatusCode(500, new { error = "Failed to synthesize speech" });
        }
    }

    [HttpPost("synthesize/personality")]
    public async Task<ActionResult<SynthesisResult>> SynthesizeWithPersonality([FromBody] PersonalitySynthesisRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return BadRequest("Text is required");
            }

            var result = await _ttsService.SynthesizeWithPersonalityAsync(
                request.Text,
                request.Personality,
                request.Emotion);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error synthesizing speech with personality");
            return StatusCode(500, new { error = "Failed to synthesize speech" });
        }
    }

    [HttpPost("synthesize/stream")]
    public async Task<IActionResult> StreamSynthesize([FromBody] StreamSynthesisRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return BadRequest("Text is required");
            }

            Response.Headers.Add("Content-Type", "audio/mpeg");
            Response.Headers.Add("Transfer-Encoding", "chunked");

            var result = await _ttsService.StreamSynthesizeAsync(
                request.Text,
                request.VoiceProfile,
                HttpContext.RequestAborted);

            foreach (var chunk in result.AudioChunks)
            {
                await Response.Body.WriteAsync(chunk, 0, chunk.Length, HttpContext.RequestAborted);
                await Response.Body.FlushAsync();
            }

            return new EmptyResult();
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Stream synthesis cancelled");
            return StatusCode(499, "Client closed request");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in stream synthesis");
            return StatusCode(500, new { error = "Failed to stream synthesize speech" });
        }
    }

    [HttpGet("audio/{sessionId}")]
    public async Task<ActionResult<List<string>>> GetSessionAudio(string sessionId)
    {
        try
        {
            var audioFiles = await _audioStorage.ListAudioFilesAsync(sessionId);
            return Ok(audioFiles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing audio files for session {SessionId}", sessionId);
            return StatusCode(500, new { error = "Failed to list audio files" });
        }
    }

    [HttpDelete("audio")]
    public async Task<ActionResult> DeleteAudio([FromQuery] string audioUrl)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(audioUrl))
            {
                return BadRequest("Audio URL is required");
            }

            var deleted = await _audioStorage.DeleteAudioAsync(audioUrl);
            if (deleted)
            {
                return NoContent();
            }

            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting audio file");
            return StatusCode(500, new { error = "Failed to delete audio file" });
        }
    }

    [HttpGet("voices")]
    public async Task<ActionResult<Dictionary<string, VoiceProfile>>> GetVoices()
    {
        try
        {
            var profiles = await _personalityService.GetAllProfilesAsync();
            return Ok(profiles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting voice profiles");
            return StatusCode(500, new { error = "Failed to get voice profiles" });
        }
    }

    [HttpGet("personalities")]
    public ActionResult<Dictionary<string, string>> GetPersonalities()
    {
        var personalities = Enum.GetValues<PersonalityProfile>()
            .ToDictionary(p => p.ToString(), p => GetPersonalityDescription(p));
        
        return Ok(personalities);
    }

    private string GetPersonalityDescription(PersonalityProfile personality)
    {
        return personality switch
        {
            PersonalityProfile.Friendly => "Warm and approachable, like a helpful friend",
            PersonalityProfile.Professional => "Clear and businesslike, focused on efficiency",
            PersonalityProfile.Casual => "Relaxed and conversational, like a colleague",
            PersonalityProfile.Minimalist => "Brief and to the point, essential information only",
            _ => "Default personality"
        };
    }
}

public class PersonalitySynthesisRequest
{
    public string Text { get; set; } = string.Empty;
    public PersonalityProfile Personality { get; set; } = PersonalityProfile.Friendly;
    public string? Emotion { get; set; }
}

public class StreamSynthesisRequest
{
    public string Text { get; set; } = string.Empty;
    public string? VoiceProfile { get; set; }
}