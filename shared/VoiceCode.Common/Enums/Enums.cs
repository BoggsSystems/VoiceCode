namespace VoiceCode.Common.Enums;

public enum UserSubscriptionTier
{
    Free,
    Basic,
    Professional,
    Enterprise
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

public enum IntentCategory
{
    CodeGeneration,
    CodeExplanation,
    CodeRefactoring,
    ErrorFixing,
    Testing,
    Documentation,
    ProjectManagement,
    SystemCommand,
    General,
    Unknown
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

public enum ProcessingStatus
{
    Pending,
    Accepted,
    Processing,
    Completed,
    Failed,
    Cancelled,
    Success,
    PartialSuccess
}

public enum FileAction
{
    Create,
    Modify,
    Delete,
    Rename,
    Move
}

public enum DiffOperation
{
    Equal,
    Insert,
    Delete
}

public enum CodeComplexity
{
    Simple,
    Moderate,
    Complex,
    VeryComplex
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