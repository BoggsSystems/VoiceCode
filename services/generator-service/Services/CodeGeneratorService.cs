using Microsoft.Extensions.Options;
using VoiceCode.Common.DTOs;
using VoiceCode.Common.Interfaces;
using VoiceCode.Common.Models;
using VoiceCode.GeneratorService.Configuration;
using VoiceCode.GeneratorService.Processors;

namespace VoiceCode.GeneratorService.Services;

public class CodeGeneratorService : ICodeGenerator
{
    private readonly IClaudeService _claudeService;
    private readonly ITemplateEngine _templateEngine;
    private readonly ICodeValidator _codeValidator;
    private readonly ICodeFormatter _codeFormatter;
    private readonly IFileOrganizer _fileOrganizer;
    private readonly IProcessorFactory _processorFactory;
    private readonly GeneratorOptions _options;
    private readonly ILogger<CodeGeneratorService> _logger;

    public CodeGeneratorService(
        IClaudeService claudeService,
        ITemplateEngine templateEngine,
        ICodeValidator codeValidator,
        ICodeFormatter codeFormatter,
        IFileOrganizer fileOrganizer,
        IProcessorFactory processorFactory,
        IOptions<GeneratorOptions> options,
        ILogger<CodeGeneratorService> logger)
    {
        _claudeService = claudeService;
        _templateEngine = templateEngine;
        _codeValidator = codeValidator;
        _codeFormatter = codeFormatter;
        _fileOrganizer = fileOrganizer;
        _processorFactory = processorFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<GeneratedFiles> GenerateFilesAsync(CodeGenerationInput input)
    {
        using var activity = System.Diagnostics.Activity.Current;
        activity?.SetTag("generator.block_count", input.CodeBlocks.Count);

        try
        {
            _logger.LogInformation("Generating files from {BlockCount} code blocks", input.CodeBlocks.Count);

            var result = new GeneratedFiles
            {
                Id = Guid.NewGuid().ToString(),
                Files = new List<GeneratedFile>(),
                Metadata = new Dictionary<string, object>
                {
                    { "timestamp", DateTime.UtcNow },
                    { "target_directory", input.TargetDirectory }
                }
            };

            // Process each code block
            foreach (var codeBlock in input.CodeBlocks)
            {
                try
                {
                    var files = await ProcessCodeBlockAsync(codeBlock, input.TargetDirectory);
                    result.Files.AddRange(files);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process code block");
                    result.Errors.Add($"Failed to process code block: {ex.Message}");
                }
            }

            // Organize files into proper structure
            if (result.Files.Any())
            {
                result.Files = await _fileOrganizer.OrganizeFilesAsync(result.Files, input.TargetDirectory);
            }

            _logger.LogInformation("Generated {FileCount} files with {ErrorCount} errors",
                result.Files.Count, result.Errors.Count);

            activity?.SetTag("generator.files_generated", result.Files.Count);
            activity?.SetTag("generator.errors", result.Errors.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Code generation failed");
            throw;
        }
    }

    public async Task<ValidationResult> ValidateCodeAsync(string code, string language)
    {
        try
        {
            var processor = _processorFactory.GetProcessor(language);
            if (processor == null)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    Error = $"No processor available for language: {language}"
                };
            }

            return await processor.ValidateAsync(code);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Code validation failed for language {Language}", language);
            return new ValidationResult
            {
                IsValid = false,
                Error = ex.Message
            };
        }
    }

    public async Task<string> FormatCodeAsync(string code, string language)
    {
        try
        {
            if (!_options.FormatGeneratedCode)
            {
                return code;
            }

            return await _codeFormatter.FormatAsync(code, language);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Code formatting failed for language {Language}, returning unformatted", language);
            return code;
        }
    }

    private async Task<List<GeneratedFile>> ProcessCodeBlockAsync(CodeBlock codeBlock, string targetDirectory)
    {
        var files = new List<GeneratedFile>();

        // Determine language and processor
        var processor = _processorFactory.GetProcessor(codeBlock.Language);
        if (processor == null)
        {
            _logger.LogWarning("No processor for language {Language}, using generic processing", codeBlock.Language);
            files.Add(CreateGenericFile(codeBlock, targetDirectory));
            return files;
        }

        // Process the code block
        var processedFiles = await processor.ProcessAsync(codeBlock);

        foreach (var file in processedFiles)
        {
            // Apply templates if available
            if (!string.IsNullOrEmpty(file.Template))
            {
                file.Content = await _templateEngine.RenderAsync(file.Template, new
                {
                    Code = file.Content,
                    FileName = file.FileName,
                    Language = codeBlock.Language,
                    Metadata = file.Metadata
                });
            }

            // Validate if enabled
            if (_options.ValidateGeneratedCode)
            {
                var validation = await ValidateCodeAsync(file.Content, codeBlock.Language);
                if (!validation.IsValid)
                {
                    file.ValidationErrors.Add(validation.Error ?? "Unknown validation error");
                }
            }

            // Format if enabled
            if (_options.FormatGeneratedCode)
            {
                file.Content = await FormatCodeAsync(file.Content, codeBlock.Language);
            }

            files.Add(file);
        }

        return files;
    }

    private GeneratedFile CreateGenericFile(CodeBlock codeBlock, string targetDirectory)
    {
        var extension = GetFileExtension(codeBlock.Language);
        var fileName = codeBlock.FileName ?? $"generated_{Guid.NewGuid():N}.{extension}";

        return new GeneratedFile
        {
            FileName = fileName,
            FilePath = Path.Combine(targetDirectory, fileName),
            Content = codeBlock.Content,
            Language = codeBlock.Language,
            FileType = DetermineFileType(fileName),
            Size = System.Text.Encoding.UTF8.GetByteCount(codeBlock.Content)
        };
    }

    private string GetFileExtension(string language)
    {
        if (_options.Languages.TryGetValue(language.ToLower(), out var settings))
        {
            return settings.FileExtension;
        }

        return language.ToLower() switch
        {
            "csharp" or "c#" => "cs",
            "javascript" => "js",
            "typescript" => "ts",
            "python" => "py",
            "java" => "java",
            "go" => "go",
            "rust" => "rs",
            "cpp" or "c++" => "cpp",
            "html" => "html",
            "css" => "css",
            "sql" => "sql",
            "json" => "json",
            "xml" => "xml",
            "yaml" => "yml",
            _ => "txt"
        };
    }

    private string DetermineFileType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLower();
        return extension switch
        {
            ".cs" => "class",
            ".js" or ".ts" => "module",
            ".py" => "module",
            ".java" => "class",
            ".go" => "package",
            ".html" or ".htm" => "markup",
            ".css" or ".scss" or ".sass" => "stylesheet",
            ".json" or ".xml" or ".yml" or ".yaml" => "config",
            ".sql" => "script",
            ".md" => "documentation",
            _ => "code"
        };
    }
}

public class GeneratedFiles
{
    public string Id { get; set; } = string.Empty;
    public List<GeneratedFile> Files { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class GeneratedFile
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public long Size { get; set; }
    public string? Template { get; set; }
    public List<string> ValidationErrors { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}