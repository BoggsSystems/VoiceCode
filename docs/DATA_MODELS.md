# VoiceCode Data Models and Interfaces

## Overview

This document defines all data models, interfaces, and contracts used across the VoiceCode system. All models are designed for cross-service compatibility and efficient serialization.

## Core Domain Models

### 1. User and Authentication

```csharp
// shared/VoiceCode.Common/Models/User.cs
namespace VoiceCode.Common.Models
{
    public class User
    {
        public string Id { get; set; }
        public string Email { get; set; }
        public string DisplayName { get; set; }
        public UserSubscriptionTier SubscriptionTier { get; set; }
        public UserPreferences Preferences { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastActiveAt { get; set; }
        public Dictionary<string, object> Metadata { get; set; }
    }

    public enum UserSubscriptionTier
    {
        Free,
        Basic,
        Professional,
        Enterprise
    }

    public class UserPreferences
    {
        public string PreferredLanguage { get; set; } = "C#";
        public string CodingStyle { get; set; } = "Clean";
        public string PreferredIDE { get; set; } = "VSCode";
        public VoiceSettings VoiceSettings { get; set; } = new();
        public NotificationSettings NotificationSettings { get; set; } = new();
        public List<string> AllowedRepositories { get; set; } = new();
        public Dictionary<string, string> CustomSettings { get; set; } = new();
    }

    public class VoiceSettings
    {
        public string Voice { get; set; } = "en-US-JennyNeural";
        public double Speed { get; set; } = 1.0;
        public double Pitch { get; set; } = 1.0;
        public string InputLanguage { get; set; } = "en-US";
        public bool AutoDetectLanguage { get; set; } = false;
    }

    public class NotificationSettings
    {
        public bool EnablePushNotifications { get; set; } = true;
        public bool EnableEmailNotifications { get; set; } = false;
        public bool NotifyOnCompletion { get; set; } = true;
        public bool NotifyOnError { get; set; } = true;
    }
}
```

### 2. Voice Processing Models

```csharp
// shared/VoiceCode.Common/Models/Voice.cs
namespace VoiceCode.Common.Models
{
    public class VoiceCommand
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string UserId { get; set; }
        public string SessionId { get; set; }
        public byte[] AudioData { get; set; }
        public string AudioFormat { get; set; } = "wav";
        public int SampleRate { get; set; } = 16000;
        public string Language { get; set; } = "en-US";
        public Dictionary<string, object> Context { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class TranscriptionResult
    {
        public string Id { get; set; }
        public string Transcript { get; set; }
        public double Confidence { get; set; }
        public string Language { get; set; }
        public double DurationMs { get; set; }
        public List<TranscriptionAlternative> Alternatives { get; set; }
        public List<TranscriptionWord> Words { get; set; }
    }

    public class TranscriptionAlternative
    {
        public string Transcript { get; set; }
        public double Confidence { get; set; }
    }

    public class TranscriptionWord
    {
        public string Word { get; set; }
        public double StartTime { get; set; }
        public double EndTime { get; set; }
        public double Confidence { get; set; }
    }
}
```

### 3. Intent and Context Models

```csharp
// shared/VoiceCode.Common/Models/Intent.cs
namespace VoiceCode.Common.Models
{
    public class Intent
    {
        public IntentType Type { get; set; }
        public double Confidence { get; set; }
        public Dictionary<string, object> Parameters { get; set; }
        public List<string> Entities { get; set; }
    }

    public enum IntentType
    {
        CodeGeneration,
        CodeExplanation,
        BugFix,
        Refactor,
        TestGeneration,
        Documentation,
        FileOperation,
        Search,
        Unknown
    }

    public class UserContext
    {
        public string UserId { get; set; }
        public string SessionId { get; set; }
        public List<ContextEntry> RecentInteractions { get; set; }
        public UserPreferences Preferences { get; set; }
        public Dictionary<string, object> SessionData { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public class ContextEntry
    {
        public string Id { get; set; }
        public string Transcript { get; set; }
        public Intent Intent { get; set; }
        public string Result { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<string, object> Metadata { get; set; }
    }
}
```

### 4. Code Generation Models

```csharp
// shared/VoiceCode.Common/Models/CodeGeneration.cs
namespace VoiceCode.Common.Models
{
    public class CodeGenerationRequest
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Instruction { get; set; }
        public string Context { get; set; }
        public string Language { get; set; }
        public string CodingStyle { get; set; }
        public int MaxTokens { get; set; } = 4000;
        public Dictionary<string, object> AdditionalContext { get; set; }
        public List<string> ExistingFiles { get; set; }
    }

    public class CodeGenerationResponse
    {
        public string Id { get; set; }
        public List<CodeBlock> Code { get; set; }
        public string Explanation { get; set; }
        public double Confidence { get; set; }
        public List<string> SuggestedFiles { get; set; }
        public List<string> Dependencies { get; set; }
        public List<string> Warnings { get; set; }
        public DateTime CreatedAt { get; set; }
        public CodeMetrics Metrics { get; set; }
    }

    public class CodeBlock
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Language { get; set; }
        public string Content { get; set; }
        public string FileName { get; set; }
        public int LineCount { get; set; }
        public CodeComplexity EstimatedComplexity { get; set; }
        public List<CodeAnnotation> Annotations { get; set; }
    }

    public class CodeAnnotation
    {
        public int Line { get; set; }
        public string Type { get; set; } // "comment", "warning", "suggestion"
        public string Message { get; set; }
    }

    public enum CodeComplexity
    {
        Simple,
        Moderate,
        Complex,
        VeryComplex
    }

    public class CodeMetrics
    {
        public int TotalLines { get; set; }
        public int CommentLines { get; set; }
        public int BlankLines { get; set; }
        public double CyclomaticComplexity { get; set; }
        public int EstimatedExecutionTime { get; set; } // in milliseconds
    }
}
```

### 5. File Management Models

```csharp
// shared/VoiceCode.Common/Models/FileManagement.cs
namespace VoiceCode.Common.Models
{
    public class FileChange
    {
        public string Path { get; set; }
        public FileAction Action { get; set; }
        public string Content { get; set; }
        public string OriginalContent { get; set; }
        public List<DiffSegment> Diff { get; set; }
        public List<string> ConflictMarkers { get; set; }
        public FileMetadata Metadata { get; set; }
    }

    public enum FileAction
    {
        Create,
        Modify,
        Delete,
        Rename,
        Move
    }

    public class DiffSegment
    {
        public DiffOperation Operation { get; set; }
        public string Text { get; set; }
        public int StartLine { get; set; }
        public int EndLine { get; set; }
    }

    public enum DiffOperation
    {
        Equal,
        Insert,
        Delete
    }

    public class FileMetadata
    {
        public string MimeType { get; set; }
        public long Size { get; set; }
        public string Encoding { get; set; } = "UTF-8";
        public DateTime LastModified { get; set; }
        public string Checksum { get; set; }
    }

    public class GeneratedFiles
    {
        public List<FileChange> Files { get; set; }
        public string Summary { get; set; }
        public string CommitMessage { get; set; }
        public List<string> Errors { get; set; }
        public FileGenerationStatistics Statistics { get; set; }
    }

    public class FileGenerationStatistics
    {
        public int FilesCreated { get; set; }
        public int FilesModified { get; set; }
        public int FilesDeleted { get; set; }
        public int TotalLinesAdded { get; set; }
        public int TotalLinesRemoved { get; set; }
        public Dictionary<string, int> FilesByLanguage { get; set; }
    }
}
```

### 6. Processing Pipeline Models

```csharp
// shared/VoiceCode.Common/Models/Processing.cs
namespace VoiceCode.Common.Models
{
    public class ProcessingRequest
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string UserId { get; set; }
        public string SessionId { get; set; }
        public ProcessingType Type { get; set; }
        public object Payload { get; set; }
        public ProcessingPriority Priority { get; set; } = ProcessingPriority.Normal;
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
        public Dictionary<string, string> Metadata { get; set; }
    }

    public enum ProcessingType
    {
        VoiceCommand,
        TextCommand,
        FileGeneration,
        CodeAnalysis
    }

    public enum ProcessingPriority
    {
        Low,
        Normal,
        High,
        Critical
    }

    public class ProcessingResult
    {
        public string Id { get; set; }
        public string RequestId { get; set; }
        public string UserId { get; set; }
        public string SessionId { get; set; }
        public ProcessingStatus Status { get; set; }
        public string Summary { get; set; }
        public object Result { get; set; }
        public string AudioUrl { get; set; }
        public List<FileChange> Files { get; set; }
        public string TargetRepository { get; set; }
        public string CommitMessage { get; set; }
        public bool RequiresLocalExecution { get; set; }
        public bool SendNotification { get; set; }
        public DateTime ProcessedAt { get; set; }
        public ProcessingMetrics Metrics { get; set; }
    }

    public enum ProcessingStatus
    {
        Pending,
        Accepted,
        Processing,
        Completed,
        Failed,
        Cancelled
    }

    public class ProcessingMetrics
    {
        public long ProcessingTimeMs { get; set; }
        public int TokensUsed { get; set; }
        public double EstimatedCost { get; set; }
        public Dictionary<string, long> StepDurations { get; set; }
    }
}
```

### 7. Message Queue Models

```csharp
// shared/VoiceCode.Common/Models/Messaging.cs
namespace VoiceCode.Common.Models
{
    public class ServiceBusMessage<T>
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string CorrelationId { get; set; }
        public string SessionId { get; set; }
        public T Body { get; set; }
        public Dictionary<string, object> Properties { get; set; }
        public DateTime EnqueuedTimeUtc { get; set; }
        public int DeliveryCount { get; set; }
    }

    public class ProcessingMessage
    {
        public string Id { get; set; }
        public string UserId { get; set; }
        public string SessionId { get; set; }
        public Intent Intent { get; set; }
        public string Prompt { get; set; }
        public UserContext Context { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class MacAgentCommand
    {
        public string CommandId { get; set; } = Guid.NewGuid().ToString();
        public string UserId { get; set; }
        public string Action { get; set; }
        public List<FileChange> Files { get; set; }
        public string Repository { get; set; }
        public string CommitMessage { get; set; }
        public bool AutoCommit { get; set; }
        public int? LineNumber { get; set; }
        public Dictionary<string, object> Parameters { get; set; }
    }
}
```

### 8. Real-time Communication Models

```csharp
// shared/VoiceCode.Common/Models/Realtime.cs
namespace VoiceCode.Common.Models
{
    public class WebSocketMessage
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public WebSocketMessageType Type { get; set; }
        public string SessionId { get; set; }
        public object Data { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public enum WebSocketMessageType
    {
        Connected,
        Disconnected,
        StatusUpdate,
        ProgressUpdate,
        ResultReady,
        Error,
        Ping,
        Pong
    }

    public class StatusUpdate
    {
        public string RequestId { get; set; }
        public string Status { get; set; }
        public int Progress { get; set; }
        public string Message { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class ResultNotification
    {
        public string RequestId { get; set; }
        public bool Success { get; set; }
        public string Summary { get; set; }
        public string ResultUrl { get; set; }
        public List<string> GeneratedFiles { get; set; }
    }
}
```

### 9. API Response Models

```csharp
// shared/VoiceCode.Common/Models/Api.cs
namespace VoiceCode.Common.Models
{
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public T Data { get; set; }
        public ApiError Error { get; set; }
        public string RequestId { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public Dictionary<string, object> Metadata { get; set; }
    }

    public class ApiError
    {
        public string Code { get; set; }
        public string Message { get; set; }
        public Dictionary<string, object> Details { get; set; }
        public List<ValidationError> ValidationErrors { get; set; }
    }

    public class ValidationError
    {
        public string Field { get; set; }
        public string Message { get; set; }
        public object AttemptedValue { get; set; }
    }

    public class PagedResponse<T>
    {
        public List<T> Items { get; set; }
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
        public bool HasPreviousPage => PageNumber > 1;
        public bool HasNextPage => PageNumber < TotalPages;
    }
}
```

### 10. Monitoring and Analytics Models

```csharp
// shared/VoiceCode.Common/Models/Analytics.cs
namespace VoiceCode.Common.Models
{
    public class UsageMetrics
    {
        public string UserId { get; set; }
        public DateTime Period { get; set; }
        public int VoiceCommandsProcessed { get; set; }
        public int CodeBlocksGenerated { get; set; }
        public int FilesCreated { get; set; }
        public int FilesModified { get; set; }
        public int TokensUsed { get; set; }
        public double TotalProcessingTimeSeconds { get; set; }
        public Dictionary<string, int> CommandsByType { get; set; }
        public Dictionary<string, int> GenerationsByLanguage { get; set; }
    }

    public class SystemMetrics
    {
        public DateTime Timestamp { get; set; }
        public string ServiceName { get; set; }
        public double CpuUsagePercent { get; set; }
        public long MemoryUsageBytes { get; set; }
        public int ActiveConnections { get; set; }
        public double RequestsPerSecond { get; set; }
        public double AverageResponseTimeMs { get; set; }
        public int ErrorCount { get; set; }
        public Dictionary<string, object> CustomMetrics { get; set; }
    }

    public class AuditLog
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string UserId { get; set; }
        public string Action { get; set; }
        public string ResourceType { get; set; }
        public string ResourceId { get; set; }
        public Dictionary<string, object> Changes { get; set; }
        public string IpAddress { get; set; }
        public string UserAgent { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
```

## Service Interfaces

### 1. Core Service Interfaces

```csharp
// shared/VoiceCode.Common/Interfaces/IServices.cs
namespace VoiceCode.Common.Interfaces
{
    public interface ISTTService
    {
        Task<TranscriptionResult> TranscribeAsync(byte[] audioData, string language = "en-US");
        Task<List<string>> GetSupportedLanguagesAsync();
    }

    public interface IClaudeService
    {
        Task<CodeGenerationResponse> GenerateCodeAsync(CodeGenerationRequest request);
        Task<string> ExplainCodeAsync(string code, string language);
        Task<string> FixCodeAsync(string code, string error, string language);
        Task<string> RefactorCodeAsync(string code, string instruction, string language);
    }

    public interface IPromptRouter
    {
        Task<RoutingResult> RouteRequestAsync(RoutingRequest request);
        Task<Intent> ClassifyIntentAsync(string transcript);
        Task<UserContext> GetContextAsync(string userId, string sessionId);
    }

    public interface ICodeGenerator
    {
        Task<GeneratedFiles> GenerateFilesAsync(CodeGenerationInput input);
        Task<ValidationResult> ValidateCodeAsync(string code, string language);
        Task<string> FormatCodeAsync(string code, string language);
    }

    public interface ITTSService
    {
        Task<AudioResult> GenerateSpeechAsync(TTSRequest request);
        Task<List<Voice>> GetAvailableVoicesAsync(string language);
    }

    public interface IDispatcher
    {
        Task<DispatchResult> DispatchResultAsync(ProcessingResult result);
        Task<bool> SendNotificationAsync(string userId, NotificationMessage message);
    }
}
```

### 2. Repository Interfaces

```csharp
// shared/VoiceCode.Common/Interfaces/IRepositories.cs
namespace VoiceCode.Common.Interfaces
{
    public interface IUserRepository
    {
        Task<User> GetByIdAsync(string userId);
        Task<User> GetByEmailAsync(string email);
        Task<User> CreateAsync(User user);
        Task<User> UpdateAsync(User user);
        Task DeleteAsync(string userId);
        Task<PagedResponse<User>> GetAllAsync(int pageNumber, int pageSize);
    }

    public interface IContextRepository
    {
        Task<UserContext> GetContextAsync(string userId, string sessionId);
        Task SaveContextAsync(UserContext context);
        Task<List<ContextEntry>> GetRecentInteractionsAsync(string userId, int count);
        Task CleanupOldContextAsync(DateTime before);
    }

    public interface ICodeHistoryRepository
    {
        Task<CodeGenerationResponse> GetByIdAsync(string id);
        Task<List<CodeGenerationResponse>> GetByUserAsync(string userId, int count);
        Task SaveAsync(CodeGenerationResponse response);
        Task<UsageMetrics> GetUsageMetricsAsync(string userId, DateTime startDate, DateTime endDate);
    }
}
```

### 3. External Service Interfaces

```csharp
// shared/VoiceCode.Common/Interfaces/IExternalServices.cs
namespace VoiceCode.Common.Interfaces
{
    public interface IAuthenticationService
    {
        Task<AuthenticationResult> AuthenticateAsync(string username, string password);
        Task<string> GenerateTokenAsync(User user);
        Task<bool> ValidateTokenAsync(string token);
        Task<User> GetUserFromTokenAsync(string token);
        Task RevokeTokenAsync(string token);
    }

    public interface IStorageService
    {
        Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType);
        Task<Stream> DownloadFileAsync(string fileUrl);
        Task DeleteFileAsync(string fileUrl);
        Task<bool> FileExistsAsync(string fileUrl);
        Task<string> GeneratePresignedUrlAsync(string fileUrl, TimeSpan expiration);
    }

    public interface ICacheService
    {
        Task<T> GetAsync<T>(string key);
        Task SetAsync<T>(string key, T value, TimeSpan? expiration = null);
        Task<bool> ExistsAsync(string key);
        Task RemoveAsync(string key);
        Task<bool> LockAsync(string key, TimeSpan duration);
        Task UnlockAsync(string key);
    }

    public interface IQueueService
    {
        Task SendMessageAsync<T>(string queueName, T message, Dictionary<string, object> properties = null);
        Task<ServiceBusMessage<T>> ReceiveMessageAsync<T>(string queueName, TimeSpan? timeout = null);
        Task CompleteMessageAsync(string queueName, string messageId);
        Task AbandonMessageAsync(string queueName, string messageId);
        Task DeadLetterMessageAsync(string queueName, string messageId, string reason);
    }
}
```

## Data Transfer Objects (DTOs)

### 1. Request DTOs

```csharp
// shared/VoiceCode.Common/DTOs/Requests.cs
namespace VoiceCode.Common.DTOs
{
    public class ProcessVoiceCommandRequest
    {
        [Required]
        public byte[] AudioData { get; set; }
        
        [Required]
        public string UserId { get; set; }
        
        [Required]
        public string SessionId { get; set; }
        
        public string Language { get; set; } = "en-US";
        
        public Dictionary<string, object> Context { get; set; }
    }

    public class GenerateCodeRequest
    {
        [Required]
        [MinLength(3)]
        public string Instruction { get; set; }
        
        [Required]
        public string Language { get; set; }
        
        public string Context { get; set; }
        
        public string Style { get; set; }
        
        [Range(100, 8000)]
        public int MaxTokens { get; set; } = 4000;
    }

    public class UpdatePreferencesRequest
    {
        public string PreferredLanguage { get; set; }
        public string CodingStyle { get; set; }
        public string PreferredIDE { get; set; }
        public VoiceSettings VoiceSettings { get; set; }
        public NotificationSettings NotificationSettings { get; set; }
    }
}
```

### 2. Response DTOs

```csharp
// shared/VoiceCode.Common/DTOs/Responses.cs
namespace VoiceCode.Common.DTOs
{
    public class ProcessingResponse
    {
        public string RequestId { get; set; }
        public ProcessingStatus Status { get; set; }
        public string Message { get; set; }
        public int? EstimatedCompletionTime { get; set; }
    }

    public class CodeResultResponse
    {
        public string Id { get; set; }
        public List<CodeBlockDto> CodeBlocks { get; set; }
        public string Explanation { get; set; }
        public List<string> SuggestedFiles { get; set; }
        public List<string> Dependencies { get; set; }
        public string AudioUrl { get; set; }
    }

    public class CodeBlockDto
    {
        public string Language { get; set; }
        public string Content { get; set; }
        public string FileName { get; set; }
    }

    public class StatusResponse
    {
        public string RequestId { get; set; }
        public ProcessingStatus Status { get; set; }
        public int Progress { get; set; }
        public object Result { get; set; }
        public string Error { get; set; }
    }
}
```

## Validation Rules

```csharp
// shared/VoiceCode.Common/Validation/ValidationRules.cs
namespace VoiceCode.Common.Validation
{
    public static class ValidationRules
    {
        public static class Audio
        {
            public const int MaxFileSizeMB = 10;
            public static readonly string[] SupportedFormats = { "wav", "mp3", "m4a", "flac" };
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
        }

        public static class API
        {
            public const int MaxRequestsPerMinute = 100;
            public const int MaxConcurrentRequests = 10;
            public const int RequestTimeoutSeconds = 300;
        }
    }
}
```

## Serialization Configuration

```csharp
// shared/VoiceCode.Common/Serialization/SerializationConfig.cs
namespace VoiceCode.Common.Serialization
{
    public static class SerializationConfig
    {
        public static JsonSerializerOptions GetDefaultOptions()
        {
            return new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Converters =
                {
                    new JsonStringEnumConverter(),
                    new DateTimeConverter(),
                    new TimeSpanConverter()
                },
                WriteIndented = false,
                MaxDepth = 32
            };
        }
    }

    public class DateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return DateTime.Parse(reader.GetString()).ToUniversalTime();
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToUniversalTime().ToString("O"));
        }
    }

    public class TimeSpanConverter : JsonConverter<TimeSpan>
    {
        public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return TimeSpan.Parse(reader.GetString());
        }

        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
```

## AutoMapper Profiles

```csharp
// shared/VoiceCode.Common/Mapping/MappingProfiles.cs
using AutoMapper;

namespace VoiceCode.Common.Mapping
{
    public class DomainToDtoProfile : Profile
    {
        public DomainToDtoProfile()
        {
            CreateMap<User, UserDto>();
            CreateMap<CodeGenerationResponse, CodeResultResponse>();
            CreateMap<CodeBlock, CodeBlockDto>();
            CreateMap<ProcessingResult, ProcessingResponse>();
            
            // Custom mappings
            CreateMap<FileChange, FileChangeDto>()
                .ForMember(dest => dest.DiffPreview, 
                    opt => opt.MapFrom(src => GenerateDiffPreview(src.Diff)));
        }

        private string GenerateDiffPreview(List<DiffSegment> diff)
        {
            // Generate a human-readable diff preview
            return string.Join("\n", diff.Take(10).Select(d => 
                $"{d.Operation}: {d.Text.Substring(0, Math.Min(50, d.Text.Length))}..."));
        }
    }

    public class DtoToDomainProfile : Profile
    {
        public DtoToDomainProfile()
        {
            CreateMap<ProcessVoiceCommandRequest, VoiceCommand>();
            CreateMap<GenerateCodeRequest, CodeGenerationRequest>();
            CreateMap<UpdatePreferencesRequest, UserPreferences>();
        }
    }
}
```

## Constants and Enumerations

```csharp
// shared/VoiceCode.Common/Constants.cs
namespace VoiceCode.Common
{
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
    }
}
```