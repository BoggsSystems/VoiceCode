namespace VoiceCode.Common.Models;

public class UsageMetrics
{
    public string UserId { get; set; } = string.Empty;
    public DateTime Period { get; set; }
    public int VoiceCommandsProcessed { get; set; }
    public int CodeBlocksGenerated { get; set; }
    public int FilesCreated { get; set; }
    public int FilesModified { get; set; }
    public int TokensUsed { get; set; }
    public double TotalProcessingTimeSeconds { get; set; }
    public Dictionary<string, int> CommandsByType { get; set; } = new();
    public Dictionary<string, int> GenerationsByLanguage { get; set; } = new();
}

public class SystemMetrics
{
    public DateTime Timestamp { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public double CpuUsagePercent { get; set; }
    public long MemoryUsageBytes { get; set; }
    public int ActiveConnections { get; set; }
    public double RequestsPerSecond { get; set; }
    public double AverageResponseTimeMs { get; set; }
    public int ErrorCount { get; set; }
    public Dictionary<string, object> CustomMetrics { get; set; } = new();
}

public class AuditLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public Dictionary<string, object> Changes { get; set; } = new();
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}