using VoiceCode.Common.Interfaces;
using VoiceCode.Common.Models;
using VoiceCode.GeneratorService.Services;

namespace VoiceCode.GeneratorService.Processors;

public class PythonProcessor : ILanguageProcessor
{
    private readonly ILogger<PythonProcessor> _logger;
    private readonly ICodeValidator _validator;
    private readonly ICodeFormatter _formatter;

    public string Language => "python";

    public PythonProcessor(
        ILogger<PythonProcessor> logger,
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
            return codeBlock.FileName.EndsWith(".py") ? codeBlock.FileName : $"{codeBlock.FileName}.py";

        return "generated.py";
    }

    private string DetermineFileType(string content)
    {
        if (content.Contains("class ") && content.Contains("Test"))
            return "test";
        if (content.Contains("def ") && content.Contains("test_"))
            return "test";
        if (content.Contains("class "))
            return "class";
        if (content.Contains("def "))
            return "module";
        return "script";
    }
}