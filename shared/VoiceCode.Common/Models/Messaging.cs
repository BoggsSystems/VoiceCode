namespace VoiceCode.Common.Models;

public class ServiceBusMessage<T>
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CorrelationId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public T? Body { get; set; }
    public Dictionary<string, object> Properties { get; set; } = new();
    public DateTime EnqueuedTimeUtc { get; set; }
    public int DeliveryCount { get; set; }
}

public class ProcessingMessage
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public Intent Intent { get; set; } = new();
    public string Prompt { get; set; } = string.Empty;
    public UserContext Context { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

public class MacAgentCommand
{
    public string CommandId { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public List<FileChange> Files { get; set; } = new();
    public string Repository { get; set; } = string.Empty;
    public string CommitMessage { get; set; } = string.Empty;
    public bool AutoCommit { get; set; }
    public int? LineNumber { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();
}