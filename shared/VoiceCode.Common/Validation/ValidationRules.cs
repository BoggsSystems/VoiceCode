namespace VoiceCode.Common.Validation;

public static class ValidationRules
{
    public static class Audio
    {
        public const int MaxFileSizeMB = 10;
        public static readonly string[] SupportedFormats = { "wav", "mp3", "m4a", "flac", "webm" };
        public static readonly int[] SupportedSampleRates = { 8000, 16000, 44100, 48000 };
    }

    public static class Code
    {
        public const int MaxInstructionLength = 1000;
        public const int MinInstructionLength = 3;
        public const int MaxContextLength = 5000;
        public const int MaxTokens = 8000;
        public const int MinTokens = 100;
    }

    public static class Files
    {
        public const int MaxFileNameLength = 255;
        public const int MaxPathLength = 1024;
        public const int MaxFileSizeMB = 50;
        public static readonly string[] ForbiddenExtensions = { ".exe", ".dll", ".so", ".dylib" };
        public static readonly string[] ForbiddenPaths = { "/System", "/Windows", "/bin", "/etc" };
    }

    public static class API
    {
        public const int MaxRequestsPerMinute = 100;
        public const int MaxConcurrentRequests = 10;
        public const int RequestTimeoutSeconds = 300;
        public const int MaxPageSize = 100;
        public const int DefaultPageSize = 20;
    }

    public static class Session
    {
        public const int MaxSessionDurationMinutes = 60;
        public const int MaxInteractionsPerSession = 100;
        public const int SessionIdleTimeoutMinutes = 15;
    }

    public static class Security
    {
        public const int MinPasswordLength = 8;
        public const int MaxPasswordLength = 128;
        public const int MaxLoginAttempts = 5;
        public const int LoginLockoutMinutes = 30;
        public const int TokenExpirationMinutes = 60;
    }
}