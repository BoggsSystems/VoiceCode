using Microsoft.AspNetCore.Mvc;

namespace VoiceCode.VoiceIntelligenceService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StatusController : ControllerBase
{
    private readonly ILogger<StatusController> _logger;

    public StatusController(ILogger<StatusController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            service = "Voice Intelligence Service",
            status = "operational",
            timestamp = DateTime.UtcNow,
            capabilities = new[]
            {
                "Multi-worker result synthesis",
                "GPT-4 Turbo powered responses",
                "Voice-optimized output",
                "TTS queue integration"
            }
        });
    }

    [HttpPost("test-synthesis")]
    public async Task<IActionResult> TestSynthesis([FromBody] TestSynthesisRequest request)
    {
        _logger.LogInformation("Test synthesis requested");
        
        // This would be used for testing the synthesis without going through queues
        return Ok(new
        {
            message = "Test endpoint - would synthesize response",
            originalCommand = request.Command,
            workerCount = request.WorkerResults?.Count ?? 0
        });
    }
}

public class TestSynthesisRequest
{
    public string Command { get; set; } = string.Empty;
    public List<object>? WorkerResults { get; set; }
}