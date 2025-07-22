using VoiceCode.Common.Enums;

namespace VoiceCode.Common.Models;

public class User
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public UserSubscriptionTier SubscriptionTier { get; set; }
    public UserPreferences Preferences { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime LastActiveAt { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
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