using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace VoiceCode.DispatcherService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthTestController : ControllerBase
{
    private readonly ILogger<AuthTestController> _logger;

    public AuthTestController(ILogger<AuthTestController> logger)
    {
        _logger = logger;
    }

    [HttpGet("check")]
    [AllowAnonymous]
    public IActionResult CheckAuth()
    {
        var authHeader = Request.Headers["Authorization"].ToString();
        var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
        var claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList();
        
        _logger.LogInformation("Auth check - Header: {Header}, IsAuth: {IsAuth}, Claims: {Claims}", 
            authHeader, isAuthenticated, claims);
        
        return Ok(new 
        { 
            authHeader = authHeader,
            isAuthenticated = isAuthenticated,
            userName = User.Identity?.Name,
            claims = claims,
            timestamp = DateTime.UtcNow
        });
    }

    [HttpGet("check-protected")]
    [Authorize]
    public IActionResult CheckProtectedAuth()
    {
        return Ok(new 
        { 
            message = "You are authorized!",
            userName = User.Identity?.Name,
            claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList()
        });
    }
}