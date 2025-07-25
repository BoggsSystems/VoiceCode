using VoiceCode.OrchestratorService.Models;

namespace VoiceCode.OrchestratorService.Services;

public class CodeAnalysisService : ICodeAnalysisService
{
    private readonly ILogger<CodeAnalysisService> _logger;

    public CodeAnalysisService(ILogger<CodeAnalysisService> logger)
    {
        _logger = logger;
    }

    public Task<List<CodeChange>> AnalyzeCodeBlocksAsync(List<CodeBlock> codeBlocks)
    {
        var changes = new List<CodeChange>();

        foreach (var block in codeBlocks)
        {
            changes.Add(new CodeChange
            {
                FileName = block.FileName ?? "Unknown",
                ChangeType = "Modified",
                Summary = $"Code changes in {block.Language} file",
                LinesAdded = CountLines(block.Code),
                LinesRemoved = 0,
                KeyElements = ExtractKeyElements(block.Code, block.Language)
            });
        }

        return Task.FromResult(changes);
    }

    private int CountLines(string code)
    {
        if (string.IsNullOrEmpty(code)) return 0;
        return code.Split('\n').Length;
    }

    private List<string> ExtractKeyElements(string code, string language)
    {
        var elements = new List<string>();
        
        // Simple extraction based on language
        if (language?.ToLower() == "csharp" || language?.ToLower() == "c#")
        {
            if (code.Contains("class ")) elements.Add("Classes");
            if (code.Contains("interface ")) elements.Add("Interfaces");
            if (code.Contains("public ") || code.Contains("private ")) elements.Add("Methods");
        }
        else if (language?.ToLower() == "javascript" || language?.ToLower() == "typescript")
        {
            if (code.Contains("function ")) elements.Add("Functions");
            if (code.Contains("class ")) elements.Add("Classes");
            if (code.Contains("const ") || code.Contains("let ")) elements.Add("Variables");
        }

        return elements;
    }
}