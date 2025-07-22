using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;
using VoiceCode.Common.DTOs;
using VoiceCode.Common.Interfaces;
using VoiceCode.Common.Models;
using VoiceCode.GeneratorService.Processors;
using VoiceCode.GeneratorService.Services;

namespace VoiceCode.GeneratorService.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
[RequiredScope(RequiredScopesConfigurationKey = "AzureAd:Scopes")]
public class GeneratorController : ControllerBase
{
    private readonly ICodeGenerator _codeGenerator;
    private readonly ITemplateEngine _templateEngine;
    private readonly IProcessorFactory _processorFactory;
    private readonly ILogger<GeneratorController> _logger;

    public GeneratorController(
        ICodeGenerator codeGenerator,
        ITemplateEngine templateEngine,
        IProcessorFactory processorFactory,
        ILogger<GeneratorController> logger)
    {
        _codeGenerator = codeGenerator;
        _templateEngine = templateEngine;
        _processorFactory = processorFactory;
        _logger = logger;
    }

    [HttpPost("generate")]
    public async Task<ActionResult<GeneratedFiles>> GenerateFiles([FromBody] CodeGenerationInput input)
    {
        var requestId = Guid.NewGuid().ToString();
        using var activity = System.Diagnostics.Activity.Current;
        activity?.SetTag("request.id", requestId);

        try
        {
            _logger.LogInformation("Processing generation request {RequestId} with {BlockCount} code blocks",
                requestId, input.CodeBlocks.Count);

            var result = await _codeGenerator.GenerateFilesAsync(input);
            
            _logger.LogInformation("Generated {FileCount} files for request {RequestId}",
                result.Files.Count, requestId);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Generation failed for request {RequestId}", requestId);
            return StatusCode(500, new { error = "Generation failed", requestId });
        }
    }

    [HttpPost("validate")]
    public async Task<ActionResult<ValidationResult>> ValidateCode([FromBody] ValidateRequest request)
    {
        try
        {
            var result = await _codeGenerator.ValidateCodeAsync(request.Code, request.Language);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Validation failed");
            return StatusCode(500, new { error = "Validation failed" });
        }
    }

    [HttpPost("format")]
    public async Task<ActionResult<FormatResponse>> FormatCode([FromBody] FormatRequest request)
    {
        try
        {
            var formatted = await _codeGenerator.FormatCodeAsync(request.Code, request.Language);
            return Ok(new FormatResponse { FormattedCode = formatted });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Formatting failed");
            return StatusCode(500, new { error = "Formatting failed" });
        }
    }

    [HttpPost("preview")]
    public async Task<ActionResult<PreviewResponse>> PreviewGeneration([FromBody] PreviewRequest request)
    {
        try
        {
            // Create a temporary code block
            var codeBlock = new CodeBlock
            {
                Content = request.Code,
                Language = request.Language,
                FileName = request.FileName
            };

            var input = new CodeGenerationInput
            {
                CodeBlocks = new List<CodeBlock> { codeBlock },
                TargetDirectory = "preview"
            };

            var result = await _codeGenerator.GenerateFilesAsync(input);
            
            return Ok(new PreviewResponse
            {
                Files = result.Files,
                Errors = result.Errors
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Preview failed");
            return StatusCode(500, new { error = "Preview failed" });
        }
    }

    [HttpGet("languages")]
    public ActionResult<List<string>> GetSupportedLanguages()
    {
        var languages = _processorFactory.GetSupportedLanguages();
        return Ok(languages);
    }

    [HttpGet("templates")]
    public async Task<ActionResult<List<TemplateInfo>>> GetAvailableTemplates()
    {
        // This would normally load from a repository
        var templates = new List<TemplateInfo>
        {
            new TemplateInfo 
            { 
                Name = "csharp-class", 
                Language = "csharp", 
                Description = "C# class template",
                Variables = new[] { "className", "namespace" }
            },
            new TemplateInfo 
            { 
                Name = "typescript-component", 
                Language = "typescript", 
                Description = "TypeScript React component",
                Variables = new[] { "componentName", "props" }
            },
            new TemplateInfo 
            { 
                Name = "python-module", 
                Language = "python", 
                Description = "Python module template",
                Variables = new[] { "moduleName", "imports" }
            }
        };

        return Ok(templates);
    }

    [HttpPost("template/render")]
    public async Task<ActionResult<RenderTemplateResponse>> RenderTemplate([FromBody] RenderTemplateRequest request)
    {
        try
        {
            var rendered = await _templateEngine.RenderAsync(request.TemplateName, request.Variables);
            return Ok(new RenderTemplateResponse { RenderedContent = rendered });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Template rendering failed");
            return BadRequest(new { error = ex.Message });
        }
    }
}

// Request/Response DTOs
public class ValidateRequest
{
    public string Code { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
}

public class FormatRequest
{
    public string Code { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
}

public class FormatResponse
{
    public string FormattedCode { get; set; } = string.Empty;
}

public class PreviewRequest
{
    public string Code { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string? FileName { get; set; }
}

public class PreviewResponse
{
    public List<GeneratedFile> Files { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public class TemplateInfo
{
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string[] Variables { get; set; } = Array.Empty<string>();
}

public class RenderTemplateRequest
{
    public string TemplateName { get; set; } = string.Empty;
    public Dictionary<string, object> Variables { get; set; } = new();
}

public class RenderTemplateResponse
{
    public string RenderedContent { get; set; } = string.Empty;
}