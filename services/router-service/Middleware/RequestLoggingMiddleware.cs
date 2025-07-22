using System.Diagnostics;

namespace VoiceCode.RouterService.Middleware;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Start timing
        var stopwatch = Stopwatch.StartNew();
        
        // Log request
        _logger.LogInformation(
            "Handling request: {Method} {Path} {QueryString} from {RemoteIpAddress}",
            context.Request.Method,
            context.Request.Path,
            context.Request.QueryString,
            context.Connection.RemoteIpAddress);

        try
        {
            // Add custom headers
            context.Response.Headers.Add("X-Request-ID", context.TraceIdentifier);
            context.Response.Headers.Add("X-Response-Time", stopwatch.ElapsedMilliseconds.ToString());

            await _next(context);
        }
        finally
        {
            stopwatch.Stop();

            // Log response
            _logger.LogInformation(
                "Finished handling request: {Method} {Path} - Status: {StatusCode} - Duration: {Duration}ms",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds);

            // Add telemetry
            if (Activity.Current != null)
            {
                Activity.Current.SetTag("http.request.method", context.Request.Method);
                Activity.Current.SetTag("http.request.path", context.Request.Path);
                Activity.Current.SetTag("http.response.status_code", context.Response.StatusCode);
                Activity.Current.SetTag("http.response.duration_ms", stopwatch.ElapsedMilliseconds);
            }
        }
    }
}