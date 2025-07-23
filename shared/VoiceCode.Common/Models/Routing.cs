using VoiceCode.Common.Enums;

namespace VoiceCode.Common.Models;


public class ConversationTurn
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Role { get; set; } = string.Empty; // "user" or "assistant"
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public Intent? DetectedIntent { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

// IntentCategory enum moved to VoiceCode.Common.Enums namespace