namespace VoiceCode.Common.Exceptions;

public class VoiceCodeException : Exception
{
    public string ErrorCode { get; }
    public Dictionary<string, object> Details { get; }

    public VoiceCodeException(string message, string errorCode = "GENERAL_ERROR") 
        : base(message)
    {
        ErrorCode = errorCode;
        Details = new Dictionary<string, object>();
    }

    public VoiceCodeException(string message, string errorCode, Exception innerException) 
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        Details = new Dictionary<string, object>();
    }

    public VoiceCodeException WithDetail(string key, object value)
    {
        Details[key] = value;
        return this;
    }
}

public class ValidationException : VoiceCodeException
{
    public List<ValidationError> Errors { get; }

    public ValidationException(string message) 
        : base(message, Constants.ErrorCodes.ValidationError)
    {
        Errors = new List<ValidationError>();
    }

    public ValidationException(List<ValidationError> errors) 
        : base("Validation failed", Constants.ErrorCodes.ValidationError)
    {
        Errors = errors;
    }
}

public class AuthenticationException : VoiceCodeException
{
    public AuthenticationException(string message) 
        : base(message, Constants.ErrorCodes.AuthenticationError)
    {
    }
}

public class AuthorizationException : VoiceCodeException
{
    public AuthorizationException(string message) 
        : base(message, Constants.ErrorCodes.AuthorizationError)
    {
    }
}

public class NotFoundException : VoiceCodeException
{
    public NotFoundException(string resourceType, string resourceId) 
        : base($"{resourceType} with ID '{resourceId}' was not found", Constants.ErrorCodes.NotFound)
    {
        WithDetail("resourceType", resourceType);
        WithDetail("resourceId", resourceId);
    }
}

public class RateLimitException : VoiceCodeException
{
    public int RetryAfterSeconds { get; }

    public RateLimitException(int retryAfterSeconds) 
        : base("Rate limit exceeded", Constants.ErrorCodes.RateLimited)
    {
        RetryAfterSeconds = retryAfterSeconds;
        WithDetail("retryAfter", retryAfterSeconds);
    }
}

public class ServiceUnavailableException : VoiceCodeException
{
    public ServiceUnavailableException(string service) 
        : base($"Service '{service}' is currently unavailable", Constants.ErrorCodes.ServiceUnavailable)
    {
        WithDetail("service", service);
    }
}

public class ProcessingException : VoiceCodeException
{
    public string RequestId { get; }

    public ProcessingException(string message, string requestId) 
        : base(message, "PROCESSING_ERROR")
    {
        RequestId = requestId;
        WithDetail("requestId", requestId);
    }
}