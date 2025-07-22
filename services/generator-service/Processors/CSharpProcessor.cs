using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;
using VoiceCode.Common.Interfaces;
using VoiceCode.Common.Models;
using VoiceCode.GeneratorService.Services;

namespace VoiceCode.GeneratorService.Processors;

public class CSharpProcessor : ILanguageProcessor
{
    private readonly ILogger<CSharpProcessor> _logger;
    private readonly ICodeValidator _validator;
    private readonly ICodeFormatter _formatter;

    public string Language => "csharp";

    public CSharpProcessor(
        ILogger<CSharpProcessor> logger,
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

        try
        {
            // Parse the code to understand its structure
            var tree = CSharpSyntaxTree.ParseText(codeBlock.Content);
            var root = await tree.GetRootAsync();

            // Extract all type declarations
            var typeDeclarations = root.DescendantNodes()
                .Where(n => n is TypeDeclarationSyntax)
                .Cast<TypeDeclarationSyntax>()
                .ToList();

            if (typeDeclarations.Count > 1)
            {
                // Multiple types - split into separate files
                foreach (var typeDecl in typeDeclarations)
                {
                    var file = await CreateFileFromTypeAsync(typeDecl, codeBlock);
                    files.Add(file);
                }
            }
            else
            {
                // Single file
                var fileName = DetermineFileName(codeBlock, root);
                var file = new GeneratedFile
                {
                    FileName = fileName,
                    Content = codeBlock.Content,
                    Language = Language,
                    FileType = DetermineFileType(root),
                    Size = System.Text.Encoding.UTF8.GetByteCount(codeBlock.Content)
                };

                // Add metadata
                ExtractMetadata(root, file);
                files.Add(file);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process C# code block");
            
            // Fallback to simple file creation
            files.Add(new GeneratedFile
            {
                FileName = codeBlock.FileName ?? "Generated.cs",
                Content = codeBlock.Content,
                Language = Language,
                FileType = "class",
                Size = System.Text.Encoding.UTF8.GetByteCount(codeBlock.Content)
            });
        }

        return files;
    }

    public async Task<ValidationResult> ValidateAsync(string code)
    {
        return await _validator.ValidateAsync(code, Language);
    }

    public async Task<string> FormatAsync(string code)
    {
        return await _formatter.FormatAsync(code, Language);
    }

    private async Task<GeneratedFile> CreateFileFromTypeAsync(TypeDeclarationSyntax typeDecl, CodeBlock codeBlock)
    {
        // Extract usings from the original code
        var root = typeDecl.SyntaxTree.GetRoot();
        var usings = root.DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .ToList();

        // Get namespace
        var namespaceDecl = typeDecl.Ancestors()
            .OfType<NamespaceDeclarationSyntax>()
            .FirstOrDefault();

        // Build the file content
        var fileContent = new System.Text.StringBuilder();
        
        // Add usings
        foreach (var usingDirective in usings)
        {
            fileContent.AppendLine(usingDirective.ToString());
        }

        if (usings.Any())
            fileContent.AppendLine();

        // Add namespace and type
        if (namespaceDecl != null)
        {
            fileContent.AppendLine($"namespace {namespaceDecl.Name}");
            fileContent.AppendLine("{");
            fileContent.AppendLine(IndentCode(typeDecl.ToString()));
            fileContent.AppendLine("}");
        }
        else
        {
            fileContent.AppendLine(typeDecl.ToString());
        }

        var fileName = $"{typeDecl.Identifier.Text}.cs";
        
        return new GeneratedFile
        {
            FileName = fileName,
            Content = await FormatAsync(fileContent.ToString()),
            Language = Language,
            FileType = GetTypeCategory(typeDecl),
            Size = System.Text.Encoding.UTF8.GetByteCount(fileContent.ToString()),
            Metadata = new Dictionary<string, object>
            {
                { "typeName", typeDecl.Identifier.Text },
                { "typeKind", typeDecl.Kind().ToString() }
            }
        };
    }

    private string DetermineFileName(CodeBlock codeBlock, SyntaxNode root)
    {
        if (!string.IsNullOrEmpty(codeBlock.FileName))
            return codeBlock.FileName.EndsWith(".cs") ? codeBlock.FileName : $"{codeBlock.FileName}.cs";

        // Try to extract from the first type declaration
        var firstType = root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault();

        if (firstType != null)
            return $"{firstType.Identifier.Text}.cs";

        // Try to extract from namespace
        var namespaceDecl = root.DescendantNodes()
            .OfType<NamespaceDeclarationSyntax>()
            .FirstOrDefault();

        if (namespaceDecl != null)
        {
            var namespaceParts = namespaceDecl.Name.ToString().Split('.');
            return $"{namespaceParts.Last()}.cs";
        }

        return "Generated.cs";
    }

    private string DetermineFileType(SyntaxNode root)
    {
        var firstType = root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault();

        if (firstType == null)
            return "code";

        return GetTypeCategory(firstType);
    }

    private string GetTypeCategory(TypeDeclarationSyntax typeDecl)
    {
        var typeName = typeDecl.Identifier.Text;
        
        if (typeDecl is InterfaceDeclarationSyntax)
            return "interface";
        
        if (typeDecl is EnumDeclarationSyntax)
            return "enum";
        
        if (typeDecl is StructDeclarationSyntax)
            return "struct";

        // Check for common patterns
        if (typeName.EndsWith("Controller"))
            return "controller";
        
        if (typeName.EndsWith("Service"))
            return "service";
        
        if (typeName.EndsWith("Repository"))
            return "repository";
        
        if (typeName.EndsWith("Model") || typeName.EndsWith("Entity") || typeName.EndsWith("Dto"))
            return "model";
        
        if (typeName.EndsWith("Exception"))
            return "exception";
        
        if (typeName.EndsWith("Test") || typeName.EndsWith("Tests"))
            return "test";

        return "class";
    }

    private void ExtractMetadata(SyntaxNode root, GeneratedFile file)
    {
        // Count various elements
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Count();
        var interfaces = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>().Count();
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Count();
        var properties = root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Count();

        file.Metadata["classes"] = classes;
        file.Metadata["interfaces"] = interfaces;
        file.Metadata["methods"] = methods;
        file.Metadata["properties"] = properties;

        // Extract namespace
        var namespaceDecl = root.DescendantNodes()
            .OfType<NamespaceDeclarationSyntax>()
            .FirstOrDefault();
        
        if (namespaceDecl != null)
        {
            file.Metadata["namespace"] = namespaceDecl.Name.ToString();
        }

        // Check for async methods
        var hasAsync = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Any(m => m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.AsyncKeyword)));
        
        file.Metadata["hasAsync"] = hasAsync;
    }

    private string IndentCode(string code)
    {
        var lines = code.Split('\n');
        return string.Join("\n", lines.Select(line => "    " + line));
    }
}