using Microsoft.Extensions.Options;
using Scriban;
using Scriban.Runtime;
using VoiceCode.GeneratorService.Configuration;

namespace VoiceCode.GeneratorService.Services;

public interface ITemplateEngine
{
    Task<string> RenderAsync(string templateName, object model);
    Task<string> RenderStringAsync(string templateContent, object model);
    Task<bool> TemplateExistsAsync(string templateName);
}

public class TemplateEngineService : ITemplateEngine
{
    private readonly TemplateOptions _options;
    private readonly ILogger<TemplateEngineService> _logger;
    private readonly Dictionary<string, Template> _templateCache;

    public TemplateEngineService(
        IOptions<TemplateOptions> options,
        ILogger<TemplateEngineService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _templateCache = new Dictionary<string, Template>();
    }

    public async Task<string> RenderAsync(string templateName, object model)
    {
        try
        {
            var template = await GetTemplateAsync(templateName);
            var context = CreateContext(model);
            
            var result = await template.RenderAsync(context);
            
            _logger.LogDebug("Rendered template {TemplateName}", templateName);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render template {TemplateName}", templateName);
            throw;
        }
    }

    public async Task<string> RenderStringAsync(string templateContent, object model)
    {
        try
        {
            var template = Template.Parse(templateContent);
            
            if (template.HasErrors)
            {
                var errors = string.Join(", ", template.Messages);
                throw new InvalidOperationException($"Template parsing errors: {errors}");
            }

            var context = CreateContext(model);
            return await template.RenderAsync(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render template string");
            throw;
        }
    }

    public async Task<bool> TemplateExistsAsync(string templateName)
    {
        var templatePath = GetTemplatePath(templateName);
        return await Task.FromResult(File.Exists(templatePath));
    }

    private async Task<Template> GetTemplateAsync(string templateName)
    {
        if (_options.CacheCompiledTemplates && _templateCache.TryGetValue(templateName, out var cached))
        {
            return cached;
        }

        var templatePath = GetTemplatePath(templateName);
        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException($"Template not found: {templateName}", templatePath);
        }

        var templateContent = await File.ReadAllTextAsync(templatePath);
        var template = Template.Parse(templateContent);

        if (template.HasErrors)
        {
            var errors = string.Join(", ", template.Messages);
            throw new InvalidOperationException($"Template parsing errors in {templateName}: {errors}");
        }

        if (_options.CacheCompiledTemplates)
        {
            _templateCache[templateName] = template;
        }

        return template;
    }

    private TemplateContext CreateContext(object model)
    {
        var scriptObject = new ScriptObject();
        
        // Add global variables
        foreach (var (key, value) in _options.GlobalVariables)
        {
            scriptObject.SetValue(key, value, readOnly: true);
        }

        // Add built-in functions
        scriptObject.Import(new BuiltinFunctions());

        // Add model
        scriptObject.Import(model, renamer: null, filter: null);

        var context = new TemplateContext();
        context.PushGlobal(scriptObject);

        return context;
    }

    private string GetTemplatePath(string templateName)
    {
        // Ensure template name is safe
        templateName = Path.GetFileName(templateName);
        
        if (!templateName.EndsWith(".liquid") && !templateName.EndsWith(".scriban"))
        {
            templateName += ".scriban";
        }

        return Path.Combine(_options.TemplatesPath, templateName);
    }
}

public class BuiltinFunctions : ScriptObject
{
    public static string PascalCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        return string.Join("", input.Split('_', '-', ' ')
            .Select(word => char.ToUpper(word[0]) + word.Substring(1).ToLower()));
    }

    public static string CamelCase(string input)
    {
        var pascal = PascalCase(input);
        return string.IsNullOrEmpty(pascal) ? pascal : char.ToLower(pascal[0]) + pascal.Substring(1);
    }

    public static string SnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        return string.Concat(input.Select((x, i) => i > 0 && char.IsUpper(x) ? "_" + x : x.ToString()))
            .ToLower();
    }

    public static string KebabCase(string input)
    {
        return SnakeCase(input)?.Replace('_', '-') ?? input;
    }

    public static string Pluralize(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        if (input.EndsWith("y"))
            return input.Substring(0, input.Length - 1) + "ies";
        
        if (input.EndsWith("s") || input.EndsWith("x") || input.EndsWith("z") ||
            input.EndsWith("ch") || input.EndsWith("sh"))
            return input + "es";
        
        return input + "s";
    }

    public static string Singularize(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        if (input.EndsWith("ies"))
            return input.Substring(0, input.Length - 3) + "y";
        
        if (input.EndsWith("es"))
            return input.Substring(0, input.Length - 2);
        
        if (input.EndsWith("s"))
            return input.Substring(0, input.Length - 1);
        
        return input;
    }
}