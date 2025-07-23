using VoiceCode.Common.DTOs;
using VoiceCode.Common.Models;

namespace VoiceCode.Common.Interfaces;

public interface ISTTService
{
    Task<TranscriptionResult> TranscribeAsync(byte[] audioData, string language = "en-US");
    Task<List<string>> GetSupportedLanguagesAsync();
}

public interface IClaudeService
{
    Task<DTOs.CodeGenerationResponse> GenerateCodeAsync(DTOs.CodeGenerationRequest request);
    Task<string> ExplainCodeAsync(string code, string language);
    Task<string> FixCodeAsync(string code, string error, string language);
    Task<string> RefactorCodeAsync(string code, string instruction, string language);
}

public interface IPromptRouter
{
    Task<DTOs.RoutingResult> RouteRequestAsync(DTOs.RoutingRequest request);
    Task<Models.Intent> ClassifyIntentAsync(string transcript);
    Task<Models.UserContext> GetContextAsync(string userId, string sessionId);
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
    Task<DTOs.DispatchResult> DispatchResultAsync(Models.ProcessingResult result);
    Task<bool> SendNotificationAsync(string userId, NotificationMessage message);
}

// Supporting types
public class CodeGenerationInput
{
    public List<Models.CodeBlock> CodeBlocks { get; set; } = new();
    public string TargetDirectory { get; set; } = string.Empty;
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public string? Error { get; set; }
}

public class Voice
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
}

public class NotificationMessage
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public Dictionary<string, string> Data { get; set; } = new();
}

// Audio storage service interface
public interface IAudioStorageService
{
    Task<string> StoreAudioAsync(byte[] audioData, string requestId, string contentType);
    Task<byte[]> RetrieveAudioAsync(string audioUrl);
    Task DeleteAudioAsync(string audioUrl);
    Task<List<string>> ListAudioFilesAsync(DateTime? startDate = null, DateTime? endDate = null);
}

// Cache service interface is defined in IExternalServices.cs