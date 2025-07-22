using System.Net;
using System.Text.Json;

namespace VoiceCode.ClaudeService.Middleware;

public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public ErrorHandlingMiddleware(
        RequestDelegate next,
        ILogger<ErrorHandlingMiddleware> logger,
        IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred");
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";
        
        var response = new ErrorResponse
        {
            TraceId = context.TraceIdentifier,
            Timestamp = DateTime.UtcNow
        };

        switch (exception)
        {
            case InvalidOperationException invalidOp:
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                response.Message = invalidOp.Message;
                response.Type = "InvalidOperation";
                break;

            case UnauthorizedAccessException unauthorized:
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                response.Message = "Unauthorized access";
                response.Type = "Unauthorized";
                break;

            case TimeoutException timeout:
                context.Response.StatusCode = (int)HttpStatusCode.RequestTimeout;
                response.Message = "Request timed out";
                response.Type = "Timeout";
                break;

            case TaskCanceledException taskCanceled:
                context.Response.StatusCode = (int)HttpStatusCode.RequestTimeout;
                response.Message = "Request was canceled";
                response.Type = "Canceled";
                break;

            case HttpRequestException httpEx:
                context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
                response.Message = "External service error";
                response.Type = "ExternalServiceError";
                break;

            default:
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                response.Message = "An error occurred while processing your request";
                response.Type = "InternalServerError";
                break;
        }

        // Include stack trace in development
        if (_env.IsDevelopment())
        {
            response.Details = exception.ToString();
        }

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(response, jsonOptions);
        await context.Response.WriteAsync(json);
    }
}

public class ErrorResponse
{
    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string TraceId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? Details { get; set; }
}