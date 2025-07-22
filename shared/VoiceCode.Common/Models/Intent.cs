using VoiceCode.Common.Enums;

namespace VoiceCode.Common.Models;

public class Intent
{
    public string Type { get; set; } = string.Empty;
    public IntentCategory Category { get; set; }
    public double Confidence { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();
    public List<string> Entities { get; set; } = new();
    public string? OriginalText { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class IntentAnalysis
{
    public string Text { get; set; } = string.Empty;
    public Intent Intent { get; set; } = new();
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
}

public class UserContext
{
    public string UserId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public List<ContextEntry> RecentInteractions { get; set; } = new();
    public UserPreferences Preferences { get; set; } = new();
    public Dictionary<string, object> SessionData { get; set; } = new();
    public DateTime LastUpdated { get; set; }
}

public class ContextEntry
{
    public string Id { get; set; } = string.Empty;
    public string Transcript { get; set; } = string.Empty;
    public Intent Intent { get; set; } = new();
    public string Result { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}