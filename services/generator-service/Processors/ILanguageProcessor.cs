using VoiceCode.Common.Interfaces;
using VoiceCode.Common.Models;
using VoiceCode.GeneratorService.Services;

namespace VoiceCode.GeneratorService.Processors;

public interface ILanguageProcessor
{
    string Language { get; }
    Task<List<GeneratedFile>> ProcessAsync(CodeBlock codeBlock);
    Task<ValidationResult> ValidateAsync(string code);
    Task<string> FormatAsync(string code);
}

public interface IProcessorFactory
{
    ILanguageProcessor? GetProcessor(string language);
    List<string> GetSupportedLanguages();
}

public class ProcessorFactory : IProcessorFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, Type> _processors;

    public ProcessorFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _processors = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            { "csharp", typeof(CSharpProcessor) },
            { "c#", typeof(CSharpProcessor) },
            { "javascript", typeof(JavaScriptProcessor) },
            { "js", typeof(JavaScriptProcessor) },
            { "typescript", typeof(TypeScriptProcessor) },
            { "ts", typeof(TypeScriptProcessor) },
            { "python", typeof(PythonProcessor) },
            { "py", typeof(PythonProcessor) },
            { "java", typeof(JavaProcessor) }
        };
    }

    public ILanguageProcessor? GetProcessor(string language)
    {
        if (_processors.TryGetValue(language, out var processorType))
        {
            return _serviceProvider.GetService(processorType) as ILanguageProcessor;
        }
        return null;
    }

    public List<string> GetSupportedLanguages()
    {
        return _processors.Keys.ToList();
    }
}