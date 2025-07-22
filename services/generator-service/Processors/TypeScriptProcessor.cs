using VoiceCode.Common.Interfaces;
using VoiceCode.Common.Models;
using VoiceCode.GeneratorService.Services;

namespace VoiceCode.GeneratorService.Processors;

public class TypeScriptProcessor : ILanguageProcessor
{
    private readonly ILogger<TypeScriptProcessor> _logger;
    private readonly ICodeValidator _validator;
    private readonly ICodeFormatter _formatter;

    public string Language => "typescript";

    public TypeScriptProcessor(
        ILogger<TypeScriptProcessor> logger,
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
            return codeBlock.FileName.EndsWith(".ts") ? codeBlock.FileName : $"{codeBlock.FileName}.ts";

        return "generated.ts";
    }

    private string DetermineFileType(string content)
    {
        if (content.Contains("@Component") || content.Contains("extends Component"))
            return "component";
        if (content.Contains("interface "))
            return "interface";
        if (content.Contains("class ") && content.Contains("Service"))
            return "service";
        if (content.Contains("export default"))
            return "module";
        return "code";
    }
}

public class JavaScriptProcessor : TypeScriptProcessor
{
    public JavaScriptProcessor(
        ILogger<JavaScriptProcessor> logger,
        ICodeValidator validator,
        ICodeFormatter formatter) : base(logger, validator, formatter)
    {
    }

    public new string Language => "javascript";
}