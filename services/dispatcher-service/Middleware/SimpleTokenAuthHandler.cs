using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace VoiceCode.DispatcherService.Middleware;

public class SimpleTokenAuthOptions : AuthenticationSchemeOptions { }

public class SimpleTokenAuthHandler : AuthenticationHandler<SimpleTokenAuthOptions>
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SimpleTokenAuthHandler> _logger;

    public SimpleTokenAuthHandler(
        IOptionsMonitor<SimpleTokenAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
        _logger = logger.CreateLogger<SimpleTokenAuthHandler>();
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing Authorization Header"));
        }

        try
        {
            var authHeader = Request.Headers["Authorization"].ToString();
            
            if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authHeader.Substring("Bearer ".Length).Trim();
                
                // Allow any non-empty token for now (in production, validate against a list)
                if (!string.IsNullOrEmpty(token))
                {
                    var claims = new[]
                    {
                        new Claim(ClaimTypes.Name, "webapp-user"),
                        new Claim(ClaimTypes.NameIdentifier, "webapp"),
                        new Claim("auth_type", "simple_token")
                    };

                    var identity = new ClaimsIdentity(claims, Scheme.Name);
                    var principal = new ClaimsPrincipal(identity);
                    var ticket = new AuthenticationTicket(principal, Scheme.Name);

                    _logger.LogInformation("Simple token authentication successful");
                    return Task.FromResult(AuthenticateResult.Success(ticket));
                }
            }

            return Task.FromResult(AuthenticateResult.Fail("Invalid Authorization Header"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during simple token authentication");
            return Task.FromResult(AuthenticateResult.Fail(ex));
        }
    }
}