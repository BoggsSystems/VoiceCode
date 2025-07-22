using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;
using VoiceCode.Common.Models;
using VoiceCode.DispatcherService.Services;

namespace VoiceCode.DispatcherService.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
[RequiredScope(RequiredScopesConfigurationKey = "AzureAd:Scopes")]
public class SessionController : ControllerBase
{
    private readonly ILogger<SessionController> _logger;
    private readonly ISessionManager _sessionManager;

    public SessionController(
        ILogger<SessionController> logger,
        ISessionManager sessionManager)
    {
        _logger = logger;
        _sessionManager = sessionManager;
    }

    [HttpGet("{sessionId}")]
    public async Task<ActionResult<UserSession>> GetSession(string sessionId)
    {
        try
        {
            var session = await _sessionManager.GetSessionAsync(sessionId);
            if (session == null)
            {
                return NotFound();
            }

            return Ok(session);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting session {SessionId}", sessionId);
            return StatusCode(500, new { error = "Failed to retrieve session" });
        }
    }

    [HttpGet("active")]
    public async Task<ActionResult<List<UserSession>>> GetActiveSessions()
    {
        try
        {
            var sessions = await _sessionManager.GetActiveSessionsAsync();
            return Ok(sessions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active sessions");
            return StatusCode(500, new { error = "Failed to retrieve active sessions" });
        }
    }

    [HttpPost("{sessionId}/end")]
    public async Task<ActionResult> EndSession(string sessionId)
    {
        try
        {
            await _sessionManager.EndSessionAsync(sessionId);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ending session {SessionId}", sessionId);
            return StatusCode(500, new { error = "Failed to end session" });
        }
    }

    [HttpPut("{sessionId}/context")]
    public async Task<ActionResult> UpdateSessionContext(string sessionId, [FromBody] Dictionary<string, object> context)
    {
        try
        {
            var session = await _sessionManager.GetSessionAsync(sessionId);
            if (session == null)
            {
                return NotFound();
            }

            await _sessionManager.UpdateSessionContextAsync(sessionId, context);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating session context {SessionId}", sessionId);
            return StatusCode(500, new { error = "Failed to update session context" });
        }
    }
}