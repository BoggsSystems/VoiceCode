using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;
using VoiceCode.DispatcherService.Services;

namespace VoiceCode.DispatcherService.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
[RequiredScope(RequiredScopesConfigurationKey = "AzureAd:Scopes")]
public class MetricsController : ControllerBase
{
    private readonly ILogger<MetricsController> _logger;
    private readonly IMetricsService _metricsService;
    private readonly ISessionManager _sessionManager;

    public MetricsController(
        ILogger<MetricsController> logger,
        IMetricsService metricsService,
        ISessionManager sessionManager)
    {
        _logger = logger;
        _metricsService = metricsService;
        _sessionManager = sessionManager;
    }

    [HttpGet]
    public async Task<ActionResult<Dictionary<string, object>>> GetMetrics()
    {
        try
        {
            var metrics = await _metricsService.GetMetricsAsync();
            return Ok(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting metrics");
            return StatusCode(500, new { error = "Failed to retrieve metrics" });
        }
    }

    [HttpGet("sessions")]
    public async Task<ActionResult<object>> GetSessionMetrics()
    {
        try
        {
            var activeSessions = await _sessionManager.GetActiveSessionsAsync();
            var metrics = new
            {
                totalActive = activeSessions.Count,
                byUser = activeSessions.GroupBy(s => s.UserId)
                    .ToDictionary(g => g.Key, g => g.Count()),
                byState = activeSessions.GroupBy(s => s.State)
                    .ToDictionary(g => g.Key.ToString(), g => g.Count()),
                averageDuration = activeSessions
                    .Where(s => s.StartTime != default)
                    .Select(s => (DateTime.UtcNow - s.StartTime).TotalMinutes)
                    .DefaultIfEmpty(0)
                    .Average(),
                averageMessages = activeSessions.Average(s => s.MessageCount)
            };

            return Ok(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting session metrics");
            return StatusCode(500, new { error = "Failed to retrieve session metrics" });
        }
    }

    [HttpGet("health")]
    public async Task<ActionResult<object>> GetHealthMetrics()
    {
        try
        {
            var metrics = await _metricsService.GetMetricsAsync();
            
            var health = new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                uptime = DateTime.UtcNow - Program.StartTime,
                connections = new
                {
                    active = metrics.GetValueOrDefault("connections.active", 0),
                    total = metrics.GetValueOrDefault("connections.total", 0)
                },
                messages = new
                {
                    total = metrics.GetValueOrDefault("messages.audio.count", 0L) + 
                           metrics.GetValueOrDefault("messages.text.count", 0L),
                    audio = metrics.GetValueOrDefault("messages.audio.count", 0),
                    text = metrics.GetValueOrDefault("messages.text.count", 0)
                },
                errors = new
                {
                    total = metrics.Where(m => m.Key.StartsWith("errors."))
                        .Sum(m => Convert.ToInt64(m.Value))
                }
            };

            return Ok(health);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting health metrics");
            return StatusCode(500, new { error = "Failed to retrieve health metrics" });
        }
    }
}

public static class Program
{
    public static DateTime StartTime { get; } = DateTime.UtcNow;
}