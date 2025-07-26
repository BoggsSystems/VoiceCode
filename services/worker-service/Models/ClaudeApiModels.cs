using System.Text.Json.Serialization;

namespace VoiceCode.WorkerService.Models;

// Request models for Claude API
public class ClaudeApiRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "claude-3-5-sonnet-20241022";
    
    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 4096;
    
    [JsonPropertyName("messages")]
    public List<ClaudeMessage> Messages { get; set; } = new();
    
    [JsonPropertyName("system")]
    public string? System { get; set; }
    
    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.2;
}

public class ClaudeMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";
    
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

// Response models from Claude API
public class ClaudeApiResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    
    [JsonPropertyName("content")]
    public List<ClaudeContent> Content { get; set; } = new();
    
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;
    
    [JsonPropertyName("usage")]
    public ClaudeUsage? Usage { get; set; }
}

public class ClaudeContent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
    
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

public class ClaudeUsage
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; set; }
    
    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; set; }
}

// Structured response for file operations
public class ClaudeFileOperationResponse
{
    [JsonPropertyName("operations")]
    public List<ClaudeFileOperation> Operations { get; set; } = new();
    
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;
    
    [JsonPropertyName("explanation")]
    public string? Explanation { get; set; }
}

public class ClaudeFileOperation
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // "create", "edit", "delete", "read"
    
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
    
    [JsonPropertyName("content")]
    public string? Content { get; set; }
    
    [JsonPropertyName("oldContent")]
    public string? OldContent { get; set; }
    
    [JsonPropertyName("newContent")]
    public string? NewContent { get; set; }
    
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

// Repository context for Claude
public class RepositoryContext
{
    public string RootPath { get; set; } = string.Empty;
    public string? RepositoryUrl { get; set; }
    public string? CurrentBranch { get; set; }
    public List<string> RelevantFiles { get; set; } = new();
    public Dictionary<string, string> FileContents { get; set; } = new();
    public string ProjectType { get; set; } = string.Empty;
    public List<string> AvailableCommands { get; set; } = new();
}