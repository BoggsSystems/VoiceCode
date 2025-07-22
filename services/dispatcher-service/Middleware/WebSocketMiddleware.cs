namespace VoiceCode.DispatcherService.Middleware;

public class WebSocketMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<WebSocketMiddleware> _logger;

    public WebSocketMiddleware(RequestDelegate next, ILogger<WebSocketMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.WebSockets.IsWebSocketRequest)
        {
            // SignalR handles WebSocket connections, log for monitoring
            _logger.LogInformation("WebSocket connection from {RemoteIp} to {Path}",
                context.Connection.RemoteIpAddress,
                context.Request.Path);
        }

        await _next(context);
    }
}