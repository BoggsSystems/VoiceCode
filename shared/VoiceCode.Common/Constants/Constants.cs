namespace VoiceCode.Common.Constants;

public static class Constants
{
    public static class Headers
    {
        public const string RequestId = "X-Request-Id";
        public const string CorrelationId = "X-Correlation-Id";
        public const string UserId = "X-User-Id";
        public const string SessionId = "X-Session-Id";
        public const string ApiVersion = "X-API-Version";
    }

    public static class Claims
    {
        public const string UserId = "uid";
        public const string Email = "email";
        public const string Subscription = "subscription";
        public const string Permissions = "permissions";
    }

    public static class Queues
    {
        public const string VoiceProcessing = "voice-processing";
        public const string CodeGeneration = "code-generation";
        public const string FileProcessing = "file-processing";
        public const string MacAgentCommands = "mac-agent-commands";
        public const string Notifications = "notifications";
    }

    public static class Cache
    {
        public const string UserPrefix = "user:";
        public const string SessionPrefix = "session:";
        public const string ResultPrefix = "result:";
        public const string AudioPrefix = "audio:";
    }

    public static class Metrics
    {
        public const string RequestDuration = "request_duration_ms";
        public const string TokensUsed = "tokens_used";
        public const string FilesGenerated = "files_generated";
        public const string ErrorRate = "error_rate";
    }

    public static class ErrorCodes
    {
        public const string ValidationError = "VALIDATION_ERROR";
        public const string AuthenticationError = "AUTHENTICATION_ERROR";
        public const string AuthorizationError = "AUTHORIZATION_ERROR";
        public const string NotFound = "NOT_FOUND";
        public const string RateLimited = "RATE_LIMITED";
        public const string ServerError = "SERVER_ERROR";
        public const string ServiceUnavailable = "SERVICE_UNAVAILABLE";
    }

    public static class FileExtensions
    {
        public static readonly Dictionary<string, string> LanguageToExtension = new()
        {
            ["csharp"] = ".cs",
            ["c#"] = ".cs",
            ["javascript"] = ".js",
            ["typescript"] = ".ts",
            ["python"] = ".py",
            ["java"] = ".java",
            ["swift"] = ".swift",
            ["go"] = ".go",
            ["rust"] = ".rs",
            ["cpp"] = ".cpp",
            ["c++"] = ".cpp",
            ["html"] = ".html",
            ["css"] = ".css",
            ["json"] = ".json",
            ["xml"] = ".xml",
            ["yaml"] = ".yaml",
            ["sql"] = ".sql"
        };
    }
}