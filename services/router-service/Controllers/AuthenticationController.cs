using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace VoiceCode.RouterService.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/[controller]")]
public class AuthenticationController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthenticationController> _logger;
    
    // Simple in-memory store for demo purposes
    private static readonly Dictionary<string, string> _validUsers = new()
    {
        { "test@voicecode.dev", "TestPassword123!" },
        { "demo@voicecode.dev", "DemoPassword123!" }
    };

    public AuthenticationController(IConfiguration configuration, ILogger<AuthenticationController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        _logger.LogInformation("Login attempt for user: {Username}", request.Username);

        // Validate credentials
        if (!_validUsers.TryGetValue(request.Username, out var password) || password != request.Password)
        {
            _logger.LogWarning("Invalid login attempt for user: {Username}", request.Username);
            return Unauthorized(new { error = "Invalid username or password" });
        }

        // Generate JWT token
        var token = GenerateJwtToken(request.Username);
        
        _logger.LogInformation("Successful login for user: {Username}", request.Username);
        
        return Ok(new LoginResponse
        {
            Token = token,
            Username = request.Username,
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        });
    }

    [HttpGet("validate")]
    [Authorize]
    public IActionResult ValidateToken()
    {
        var username = User.Identity?.Name ?? "Unknown";
        return Ok(new { valid = true, username, claims = User.Claims.Select(c => new { c.Type, c.Value }) });
    }

    [HttpPost("simple-token")]
    [AllowAnonymous]
    public IActionResult GetSimpleToken()
    {
        // For testing: Generate a simple token without credentials
        var token = GenerateJwtToken("test@voicecode.dev");
        
        return Ok(new
        {
            token,
            type = "Bearer",
            expiresIn = 86400 // 24 hours
        });
    }
    
    [HttpPost("test-transcribe")]
    [Authorize]
    public IActionResult TestTranscribe()
    {
        // Mock transcription endpoint for testing authentication flow
        _logger.LogInformation("Test transcription called by user: {Username}", User.Identity?.Name);
        
        return Ok(new
        {
            id = Guid.NewGuid().ToString(),
            transcript = "This is a test transcription response",
            confidence = 0.95,
            language = "en-US",
            timestamp = DateTime.UtcNow,
            user = User.Identity?.Name
        });
    }

    private string GenerateJwtToken(string username)
    {
        // Get or generate a secret key (in production, this should come from Key Vault)
        var secretKey = _configuration["Authentication:JwtSecret"] ?? "VoiceCodeDevelopmentSecretKey123!ThisShouldBeInKeyVault";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Email, username),
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("sub", username),
            new Claim("aud", "voicecode-api"),
            new Claim("iss", "voicecode-auth"),
            new Claim(ClaimTypes.Role, "User"),
            new Claim("scope", "access_as_user")
        };

        var token = new JwtSecurityToken(
            issuer: "voicecode-auth",
            audience: "voicecode-api",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(24),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}