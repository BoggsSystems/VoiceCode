namespace VoiceCode.ObserverService.Models;

public class SdkStreamEvent
{
    public string SessionId { get; set; } = string.Empty;
    public string WorkerId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public Dictionary<string, object> Metadata { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public static class SdkEventTypes
{
    public const string FileRead = "file_read";
    public const string FileWrite = "file_write";
    public const string FileCreate = "file_create";
    public const string FileDelete = "file_delete";
    public const string DirectoryCreate = "directory_create";
    public const string CodeAnalysis = "code_analysis";
    public const string CodeGeneration = "code_generation";
    public const string CommandExecution = "command_execution";
    public const string Error = "error";
    public const string Progress = "progress";
    public const string Thinking = "thinking";
}