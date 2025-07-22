using VoiceCode.Common.Interfaces;
using VoiceCode.Common.Models;
using VoiceCode.GeneratorService.Services;

namespace VoiceCode.GeneratorService.Processors;

public class JavaProcessor : ILanguageProcessor
{
    private readonly ILogger<JavaProcessor> _logger;
    private readonly ICodeValidator _validator;
    private readonly ICodeFormatter _formatter;

    public string Language => "java";

    public JavaProcessor(
        ILogger<JavaProcessor> logger,
        ICodeValidator validator,
        ICodeFormatter formatter)
    {
        _logger = logger;
        _validator = validator;
        _formatter = formatter;
    }

    public async Task<List<GeneratedFile>> ProcessAsync(CodeBlock codeBlock)
    {
        var files = new List<GeneratedFile>();
        
        var fileName = DetermineFileName(codeBlock);
        var file = new GeneratedFile
        {
            FileName = fileName,
            Content = codeBlock.Content,
            Language = Language,
            FileType = DetermineFileType(codeBlock.Content),
            Size = System.Text.Encoding.UTF8.GetByteCount(codeBlock.Content)
        };

        files.Add(file);
        return await Task.FromResult(files);
    }

    public async Task<ValidationResult> ValidateAsync(string code)
    {
        return await _validator.ValidateAsync(code, Language);
    }

    public async Task<string> FormatAsync(string code)
    {
        return await _formatter.FormatAsync(code, Language);
    }

    private string DetermineFileName(CodeBlock codeBlock)
    {
        if (!string.IsNullOrEmpty(codeBlock.FileName))
            return codeBlock.FileName.EndsWith(".java") ? codeBlock.FileName : $"{codeBlock.FileName}.java";

        // Try to extract class name
        var classMatch = System.Text.RegularExpressions.Regex.Match(
            codeBlock.Content, 
            @"public\s+class\s+(\w+)");
        
        if (classMatch.Success)
            return $"{classMatch.Groups[1].Value}.java";

        return "Generated.java";
    }

    private string DetermineFileType(string content)
    {
        if (content.Contains("@Controller") || content.Contains("Controller"))
            return "controller";
        if (content.Contains("@Service") || content.Contains("Service"))
            return "service";
        if (content.Contains("@Repository") || content.Contains("Repository"))
            return "repository";
        if (content.Contains("@Entity") || content.Contains("Entity"))
            return "entity";
        if (content.Contains("interface "))
            return "interface";
        if (content.Contains("@Test") || content.Contains("Test"))
            return "test";
        return "class";
    }
}