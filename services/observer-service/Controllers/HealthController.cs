using Microsoft.AspNetCore.Mvc;

namespace VoiceCode.ObserverService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly ILogger<HealthController> _logger;

    public HealthController(ILogger<HealthController> logger)
    {
        _logger = logger;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            Status = "Healthy",
            Service = "ObserverService",
            Timestamp = DateTime.UtcNow
        });
    }
}