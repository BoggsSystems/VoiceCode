using VoiceCode.Common.Enums;

namespace VoiceCode.Common.Models;

public class CodeGenerationRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Instruction { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string CodingStyle { get; set; } = string.Empty;
    public int MaxTokens { get; set; } = 4000;
    public Dictionary<string, object> AdditionalContext { get; set; } = new();
    public List<string> ExistingFiles { get; set; } = new();
}

public class CodeGenerationResponse
{
    public string Id { get; set; } = string.Empty;
    public List<CodeBlock> Code { get; set; } = new();
    public string Explanation { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public List<string> SuggestedFiles { get; set; } = new();
    public List<string> Dependencies { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public CodeMetrics Metrics { get; set; } = new();
}

public class CodeBlock
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Language { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int LineCount { get; set; }
    public CodeComplexity EstimatedComplexity { get; set; }
    public List<CodeAnnotation> Annotations { get; set; } = new();
}

public class CodeAnnotation
{
    public int Line { get; set; }
    public string Type { get; set; } = string.Empty; // "comment", "warning", "suggestion"
    public string Message { get; set; } = string.Empty;
}

public class CodeMetrics
{
    public int TotalLines { get; set; }
    public int CommentLines { get; set; }
    public int BlankLines { get; set; }
    public double CyclomaticComplexity { get; set; }
    public int EstimatedExecutionTime { get; set; } // in milliseconds
}