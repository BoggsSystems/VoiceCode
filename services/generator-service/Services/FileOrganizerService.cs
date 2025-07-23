using System.Text.RegularExpressions;
using VoiceCode.GeneratorService.Services;
using VoiceCode.Common.Models;

namespace VoiceCode.GeneratorService.Services;

public interface IFileOrganizer
{
    Task<List<GeneratedFile>> OrganizeFilesAsync(List<GeneratedFile> files, string targetDirectory);
}

public class FileOrganizerService : IFileOrganizer
{
    private readonly ILogger<FileOrganizerService> _logger;
    private readonly Dictionary<string, string> _languageDirectories;

    public FileOrganizerService(ILogger<FileOrganizerService> logger)
    {
        _logger = logger;
        _languageDirectories = InitializeLanguageDirectories();
    }

    public async Task<List<GeneratedFile>> OrganizeFilesAsync(List<GeneratedFile> files, string targetDirectory)
    {
        try
        {
            var organizedFiles = new List<GeneratedFile>();

            foreach (var file in files)
            {
                var organizedFile = new GeneratedFile
                {
                    FileName = file.FileName,
                    Content = file.Content,
                    Language = file.Language,
                    FileType = file.FileType,
                    Size = file.Size,
                    Template = file.Template,
                    ValidationErrors = file.ValidationErrors,
                    Metadata = file.Metadata
                };

                // Determine the appropriate directory structure
                var relativePath = DetermineFilePath(file);
                organizedFile.FilePath = Path.Combine(targetDirectory, relativePath, file.FileName);

                // Ensure the directory exists
                var directory = Path.GetDirectoryName(organizedFile.FilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    organizedFile.Metadata["directory"] = directory;
                }

                organizedFiles.Add(organizedFile);
            }

            // Detect and handle project structure
            HandleProjectStructure(organizedFiles, targetDirectory);

            _logger.LogInformation("Organized {FileCount} files", organizedFiles.Count);
            return await Task.FromResult(organizedFiles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to organize files");
            return files;
        }
    }

    private string DetermineFilePath(GeneratedFile file)
    {
        // Check if file already has a path specified
        if (file.Metadata.TryGetValue("path", out var specifiedPath) && specifiedPath is string pathStr)
        {
            return pathStr;
        }

        // Determine by file type and language
        var fileType = DetermineFileCategory(file);
        
        return fileType switch
        {
            FileCategory.Source => GetSourceDirectory(file.Language),
            FileCategory.Test => GetTestDirectory(file.Language),
            FileCategory.Interface => GetInterfaceDirectory(file.Language),
            FileCategory.Model => GetModelDirectory(file.Language),
            FileCategory.Service => GetServiceDirectory(file.Language),
            FileCategory.Controller => GetControllerDirectory(file.Language),
            FileCategory.Component => GetComponentDirectory(file.Language),
            FileCategory.Configuration => "config",
            FileCategory.Documentation => "docs",
            FileCategory.Script => "scripts",
            FileCategory.Style => GetStyleDirectory(file.Language),
            FileCategory.Asset => "assets",
            _ => GetDefaultDirectory(file.Language)
        };
    }

    private FileCategory DetermineFileCategory(GeneratedFile file)
    {
        var fileName = file.FileName.ToLower();
        var content = file.Content.ToLower();

        // Test files
        if (fileName.Contains("test") || fileName.Contains("spec") || 
            content.Contains("[test]") || content.Contains("describe("))
        {
            return FileCategory.Test;
        }

        // Interface files
        if (fileName.StartsWith("i") && char.IsUpper(file.FileName[1]) ||
            content.Contains("interface ") || content.Contains("public interface"))
        {
            return FileCategory.Interface;
        }

        // Model/Entity files
        if (fileName.Contains("model") || fileName.Contains("entity") || 
            fileName.Contains("dto") || content.Contains("public class") && content.Contains("{ get; set; }"))
        {
            return FileCategory.Model;
        }

        // Service files
        if (fileName.Contains("service") || content.Contains("service"))
        {
            return FileCategory.Service;
        }

        // Controller files
        if (fileName.Contains("controller") || content.Contains("[controller]") ||
            content.Contains("@controller") || Regex.IsMatch(content, "class.*controller", RegexOptions.IgnoreCase))
        {
            return FileCategory.Controller;
        }

        // Component files (React, Angular, Vue)
        if (fileName.Contains("component") || content.Contains("@component") ||
            content.Contains("export default") || content.Contains("render()"))
        {
            return FileCategory.Component;
        }

        // Configuration files
        if (file.FileType == "config" || fileName.Contains("config") || 
            fileName.EndsWith(".json") || fileName.EndsWith(".xml") || fileName.EndsWith(".yml"))
        {
            return FileCategory.Configuration;
        }

        // Documentation
        if (fileName.EndsWith(".md") || fileName.Contains("readme"))
        {
            return FileCategory.Documentation;
        }

        // Scripts
        if (fileName.EndsWith(".sh") || fileName.EndsWith(".ps1") || 
            fileName.EndsWith(".bat") || fileName.EndsWith(".cmd"))
        {
            return FileCategory.Script;
        }

        // Styles
        if (fileName.EndsWith(".css") || fileName.EndsWith(".scss") || 
            fileName.EndsWith(".sass") || fileName.EndsWith(".less"))
        {
            return FileCategory.Style;
        }

        return FileCategory.Source;
    }

    private string GetSourceDirectory(string language)
    {
        return language.ToLower() switch
        {
            "csharp" or "c#" => "src",
            "java" => "src/main/java",
            "python" => "src",
            "javascript" or "typescript" => "src",
            "go" => "pkg",
            "rust" => "src",
            _ => "src"
        };
    }

    private string GetTestDirectory(string language)
    {
        return language.ToLower() switch
        {
            "csharp" or "c#" => "tests",
            "java" => "src/test/java",
            "python" => "tests",
            "javascript" or "typescript" => "tests",
            "go" => "test",
            "rust" => "tests",
            _ => "tests"
        };
    }

    private string GetInterfaceDirectory(string language)
    {
        return language.ToLower() switch
        {
            "csharp" or "c#" => Path.Combine("src", "Interfaces"),
            "java" => Path.Combine("src/main/java", "interfaces"),
            "typescript" => Path.Combine("src", "interfaces"),
            _ => GetSourceDirectory(language)
        };
    }

    private string GetModelDirectory(string language)
    {
        return language.ToLower() switch
        {
            "csharp" or "c#" => Path.Combine("src", "Models"),
            "java" => Path.Combine("src/main/java", "models"),
            "python" => Path.Combine("src", "models"),
            "javascript" or "typescript" => Path.Combine("src", "models"),
            _ => GetSourceDirectory(language)
        };
    }

    private string GetServiceDirectory(string language)
    {
        return language.ToLower() switch
        {
            "csharp" or "c#" => Path.Combine("src", "Services"),
            "java" => Path.Combine("src/main/java", "services"),
            "python" => Path.Combine("src", "services"),
            "javascript" or "typescript" => Path.Combine("src", "services"),
            _ => GetSourceDirectory(language)
        };
    }

    private string GetControllerDirectory(string language)
    {
        return language.ToLower() switch
        {
            "csharp" or "c#" => Path.Combine("src", "Controllers"),
            "java" => Path.Combine("src/main/java", "controllers"),
            "python" => Path.Combine("src", "controllers"),
            "javascript" or "typescript" => Path.Combine("src", "controllers"),
            _ => GetSourceDirectory(language)
        };
    }

    private string GetComponentDirectory(string language)
    {
        return language.ToLower() switch
        {
            "javascript" or "typescript" => Path.Combine("src", "components"),
            _ => GetSourceDirectory(language)
        };
    }

    private string GetStyleDirectory(string language)
    {
        return Path.Combine("src", "styles");
    }

    private string GetDefaultDirectory(string language)
    {
        if (_languageDirectories.TryGetValue(language.ToLower(), out var directory))
        {
            return directory;
        }
        return "src";
    }

    private void HandleProjectStructure(List<GeneratedFile> files, string targetDirectory)
    {
        // Detect project type based on files
        var hasPackageJson = files.Any(f => f.FileName.Equals("package.json", StringComparison.OrdinalIgnoreCase));
        var hasCsproj = files.Any(f => f.FileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
        var hasPomXml = files.Any(f => f.FileName.Equals("pom.xml", StringComparison.OrdinalIgnoreCase));
        var hasRequirementsTxt = files.Any(f => f.FileName.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase));

        if (hasPackageJson)
        {
            // Node.js/JavaScript project
            EnsureNodeProjectStructure(files);
        }
        else if (hasCsproj)
        {
            // .NET project
            EnsureDotNetProjectStructure(files);
        }
        else if (hasPomXml)
        {
            // Java Maven project
            EnsureJavaProjectStructure(files);
        }
        else if (hasRequirementsTxt)
        {
            // Python project
            EnsurePythonProjectStructure(files);
        }
    }

    private void EnsureNodeProjectStructure(List<GeneratedFile> files)
    {
        // Ensure common Node.js directories
        foreach (var file in files)
        {
            if (file.FileName == "package.json" || file.FileName == "tsconfig.json")
            {
                file.FilePath = Path.Combine(Path.GetDirectoryName(file.FilePath) ?? "", "..", file.FileName);
            }
        }
    }

    private void EnsureDotNetProjectStructure(List<GeneratedFile> files)
    {
        // Move .csproj files to root
        foreach (var file in files)
        {
            if (file.FileName.EndsWith(".csproj"))
            {
                file.FilePath = Path.Combine(Path.GetDirectoryName(file.FilePath) ?? "", "..", file.FileName);
            }
        }
    }

    private void EnsureJavaProjectStructure(List<GeneratedFile> files)
    {
        // Ensure Maven structure
        foreach (var file in files)
        {
            if (file.FileName == "pom.xml")
            {
                file.FilePath = Path.Combine(Path.GetDirectoryName(file.FilePath) ?? "", "..", file.FileName);
            }
        }
    }

    private void EnsurePythonProjectStructure(List<GeneratedFile> files)
    {
        // Ensure Python project structure
        foreach (var file in files)
        {
            if (file.FileName == "requirements.txt" || file.FileName == "setup.py")
            {
                file.FilePath = Path.Combine(Path.GetDirectoryName(file.FilePath) ?? "", "..", file.FileName);
            }
        }
    }

    private Dictionary<string, string> InitializeLanguageDirectories()
    {
        return new Dictionary<string, string>
        {
            { "csharp", "src" },
            { "c#", "src" },
            { "java", "src/main/java" },
            { "python", "src" },
            { "javascript", "src" },
            { "typescript", "src" },
            { "go", "pkg" },
            { "rust", "src" },
            { "cpp", "src" },
            { "c++", "src" },
            { "ruby", "lib" },
            { "php", "src" },
            { "swift", "Sources" },
            { "kotlin", "src/main/kotlin" }
        };
    }
}

public enum FileCategory
{
    Source,
    Test,
    Interface,
    Model,
    Service,
    Controller,
    Component,
    Configuration,
    Documentation,
    Script,
    Style,
    Asset
}