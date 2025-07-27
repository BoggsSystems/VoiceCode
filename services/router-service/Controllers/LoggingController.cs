using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json;

namespace VoiceCode.RouterService.Controllers
{
    [ApiController]
    [Route("api/logs")]
    public class LoggingController : ControllerBase
    {
        private readonly ILogger<LoggingController> _logger;
        private readonly IConfiguration _configuration;

        public LoggingController(ILogger<LoggingController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        [HttpPost("frontend")]
        [Authorize]
        public async Task<IActionResult> LogFromFrontend([FromBody] FrontendLogRequest request)
        {
            try
            {
                // Validate request
                if (request == null || request.Logs == null || !request.Logs.Any())
                {
                    return BadRequest(new { error = "No logs provided" });
                }

                // Get user info from auth token
                var userId = User.Identity?.Name ?? "anonymous";
                var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var userAgent = Request.Headers["User-Agent"].ToString();

                // Process each log entry
                foreach (var log in request.Logs)
                {
                    // Add server-side metadata
                    var enrichedLog = new
                    {
                        Frontend = new
                        {
                            Timestamp = log.Timestamp,
                            Level = log.Level,
                            Message = log.Message,
                            Component = log.Component,
                            SessionId = log.SessionId,
                            Details = log.Details
                        },
                        Server = new
                        {
                            ReceivedAt = DateTime.UtcNow,
                            UserId = userId,
                            ClientIp = clientIp,
                            UserAgent = userAgent
                        }
                    };

                    // Log at appropriate level
                    switch (log.Level?.ToLower())
                    {
                        case "error":
                            _logger.LogError("[FRONTEND] {Component} - {Message} | SessionId: {SessionId} | Details: {Details}",
                                log.Component, log.Message, log.SessionId, JsonSerializer.Serialize(enrichedLog));
                            break;
                        case "warn":
                        case "warning":
                            _logger.LogWarning("[FRONTEND] {Component} - {Message} | SessionId: {SessionId} | Details: {Details}",
                                log.Component, log.Message, log.SessionId, JsonSerializer.Serialize(enrichedLog));
                            break;
                        case "debug":
                            _logger.LogDebug("[FRONTEND] {Component} - {Message} | SessionId: {SessionId} | Details: {Details}",
                                log.Component, log.Message, log.SessionId, JsonSerializer.Serialize(enrichedLog));
                            break;
                        default: // info
                            _logger.LogInformation("[FRONTEND] {Component} - {Message} | SessionId: {SessionId} | Details: {Details}",
                                log.Component, log.Message, log.SessionId, JsonSerializer.Serialize(enrichedLog));
                            break;
                    }
                }

                return Ok(new
                {
                    success = true,
                    message = $"Successfully processed {request.Logs.Count} log entries",
                    timestamp = DateTime.UtcNow,
                    sessionId = request.Logs.FirstOrDefault()?.SessionId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing frontend logs");
                return StatusCode(500, new { error = "Failed to process logs", message = ex.Message });
            }
        }

        [HttpPost("frontend/simple")]
        public async Task<IActionResult> SimpleLogFromFrontend([FromBody] FrontendLogRequest request)
        {
            // Same as above but without authorization for testing
            try
            {
                if (request == null || request.Logs == null || !request.Logs.Any())
                {
                    return BadRequest(new { error = "No logs provided" });
                }

                var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var userAgent = Request.Headers["User-Agent"].ToString();

                foreach (var log in request.Logs)
                {
                    var enrichedLog = new
                    {
                        Frontend = log,
                        Server = new
                        {
                            ReceivedAt = DateTime.UtcNow,
                            ClientIp = clientIp,
                            UserAgent = userAgent
                        }
                    };

                    // Log at appropriate level
                    switch (log.Level?.ToLower())
                    {
                        case "error":
                            _logger.LogError("[FRONTEND] {Component} - {Message} | SessionId: {SessionId} | Details: {Details}",
                                log.Component, log.Message, log.SessionId, JsonSerializer.Serialize(enrichedLog));
                            break;
                        case "warn":
                        case "warning":
                            _logger.LogWarning("[FRONTEND] {Component} - {Message} | SessionId: {SessionId} | Details: {Details}",
                                log.Component, log.Message, log.SessionId, JsonSerializer.Serialize(enrichedLog));
                            break;
                        case "debug":
                            _logger.LogDebug("[FRONTEND] {Component} - {Message} | SessionId: {SessionId} | Details: {Details}",
                                log.Component, log.Message, log.SessionId, JsonSerializer.Serialize(enrichedLog));
                            break;
                        default: // info
                            _logger.LogInformation("[FRONTEND] {Component} - {Message} | SessionId: {SessionId} | Details: {Details}",
                                log.Component, log.Message, log.SessionId, JsonSerializer.Serialize(enrichedLog));
                            break;
                    }
                }

                return Ok(new
                {
                    success = true,
                    message = $"Successfully processed {request.Logs.Count} log entries",
                    timestamp = DateTime.UtcNow,
                    sessionId = request.Logs.FirstOrDefault()?.SessionId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing frontend logs");
                return StatusCode(500, new { error = "Failed to process logs", message = ex.Message });
            }
        }
    }

    public class FrontendLogRequest
    {
        public List<FrontendLogEntry> Logs { get; set; } = new List<FrontendLogEntry>();
    }

    public class FrontendLogEntry
    {
        public string Timestamp { get; set; } = DateTime.UtcNow.ToString("O");
        public string Level { get; set; } = "info";
        public string Message { get; set; } = string.Empty;
        public string Component { get; set; } = string.Empty;
        public string? SessionId { get; set; }
        public Dictionary<string, object>? Details { get; set; }
    }
}