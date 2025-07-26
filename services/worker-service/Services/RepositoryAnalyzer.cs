using VoiceCode.WorkerService.Models;

namespace VoiceCode.WorkerService.Services;

public interface IRepositoryAnalyzer
{
    Task<RepositoryContext> AnalyzeRepositoryAsync(string rootPath, CancellationToken cancellationToken = default);
}

public class RepositoryAnalyzer : IRepositoryAnalyzer
{
    private readonly ILogger<RepositoryAnalyzer> _logger;
    private readonly HashSet<string> _ignoreDirectories = new()
    {
        ".git", "node_modules", "bin", "obj", "dist", "build", ".next", 
        ".nuxt", "target", ".idea", ".vs", ".vscode", "__pycache__"
    };
    
    private readonly Dictionary<string, string> _projectTypeIndicators = new()
    {
        { "package.json", "Node.js/JavaScript" },
        { "pom.xml", "Java Maven" },
        { "build.gradle", "Java Gradle" },
        { "requirements.txt", "Python" },
        { "setup.py", "Python" },
        { "pyproject.toml", "Python" },
        { "*.csproj", ".NET/C#" },
        { "*.sln", ".NET/C#" },
        { "go.mod", "Go" },
        { "Cargo.toml", "Rust" },
        { "composer.json", "PHP" }
    };
    
    public RepositoryAnalyzer(ILogger<RepositoryAnalyzer> logger)
    {
        _logger = logger;
    }
    
    public async Task<RepositoryContext> AnalyzeRepositoryAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Analyzing repository at: {Path}", rootPath);
        
        var context = new RepositoryContext
        {
            RootPath = rootPath
        };
        
        // Get git info if available
        await GetGitInfoAsync(rootPath, context);
        
        // Detect project type
        context.ProjectType = await DetectProjectTypeAsync(rootPath);
        _logger.LogInformation("Detected project type: {Type}", context.ProjectType);
        
        // Get relevant files
        context.RelevantFiles = await GetRelevantFilesAsync(rootPath);
        _logger.LogInformation("Found {Count} relevant files", context.RelevantFiles.Count);
        
        // Read key files
        await ReadKeyFilesAsync(rootPath, context);
        
        // Detect available commands
        context.AvailableCommands = await DetectAvailableCommandsAsync(rootPath, context.ProjectType);
        
        return context;
    }
    
    private async Task GetGitInfoAsync(string rootPath, RepositoryContext context)
    {
        try
        {
            var gitConfigPath = Path.Combine(rootPath, ".git", "config");
            if (File.Exists(gitConfigPath))
            {
                var gitConfig = await File.ReadAllTextAsync(gitConfigPath);
                
                // Extract remote URL
                var urlMatch = System.Text.RegularExpressions.Regex.Match(gitConfig, @"url\s*=\s*(.+)");
                if (urlMatch.Success)
                {
                    context.RepositoryUrl = urlMatch.Groups[1].Value.Trim();
                }
                
                // Get current branch
                var headPath = Path.Combine(rootPath, ".git", "HEAD");
                if (File.Exists(headPath))
                {
                    var head = await File.ReadAllTextAsync(headPath);
                    var branchMatch = System.Text.RegularExpressions.Regex.Match(head, @"ref:\s*refs/heads/(.+)");
                    if (branchMatch.Success)
                    {
                        context.CurrentBranch = branchMatch.Groups[1].Value.Trim();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get git info");
        }
    }
    
    private async Task<string> DetectProjectTypeAsync(string rootPath)
    {
        foreach (var (indicator, projectType) in _projectTypeIndicators)
        {
            if (indicator.Contains('*'))
            {
                // Handle wildcards
                var pattern = indicator.Replace("*", "");
                if (Directory.GetFiles(rootPath, $"*{pattern}", SearchOption.TopDirectoryOnly).Any())
                {
                    return projectType;
                }
            }
            else
            {
                // Check for specific file
                if (File.Exists(Path.Combine(rootPath, indicator)))
                {
                    return projectType;
                }
            }
        }
        
        return "Unknown";
    }
    
    private async Task<List<string>> GetRelevantFilesAsync(string rootPath)
    {
        var relevantFiles = new List<string>();
        
        try
        {
            await GetFilesRecursiveAsync(rootPath, rootPath, relevantFiles, maxDepth: 4);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error scanning directory structure");
        }
        
        return relevantFiles
            .OrderBy(f => f)
            .Take(100) // Limit to prevent context overflow
            .ToList();
    }
    
    private async Task GetFilesRecursiveAsync(string currentPath, string rootPath, List<string> files, int depth = 0, int maxDepth = 4)
    {
        if (depth > maxDepth) return;
        
        try
        {
            var dirName = Path.GetFileName(currentPath);
            if (_ignoreDirectories.Contains(dirName))
                return;
            
            // Add files
            foreach (var file in Directory.GetFiles(currentPath))
            {
                var relativePath = Path.GetRelativePath(rootPath, file);
                var extension = Path.GetExtension(file).ToLowerInvariant();
                
                // Include source code files
                if (IsRelevantFile(extension))
                {
                    files.Add(relativePath);
                }
            }
            
            // Recurse into subdirectories
            foreach (var directory in Directory.GetDirectories(currentPath))
            {
                await GetFilesRecursiveAsync(directory, rootPath, files, depth + 1, maxDepth);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip directories we can't access
        }
    }
    
    private bool IsRelevantFile(string extension)
    {
        var relevantExtensions = new HashSet<string>
        {
            ".cs", ".js", ".ts", ".jsx", ".tsx", ".java", ".py", ".go", ".rs",
            ".cpp", ".c", ".h", ".hpp", ".php", ".rb", ".swift", ".kt", ".scala",
            ".json", ".xml", ".yaml", ".yml", ".toml", ".properties", ".config",
            ".md", ".txt", ".gitignore", ".dockerfile", ".sh", ".ps1"
        };
        
        return relevantExtensions.Contains(extension);
    }
    
    private async Task ReadKeyFilesAsync(string rootPath, RepositoryContext context)
    {
        var keyFiles = new List<string>
        {
            "README.md", "package.json", "pom.xml", "build.gradle", 
            "requirements.txt", "go.mod", "Cargo.toml", "composer.json"
        };
        
        foreach (var keyFile in keyFiles)
        {
            var fullPath = Path.Combine(rootPath, keyFile);
            if (File.Exists(fullPath))
            {
                try
                {
                    var content = await File.ReadAllTextAsync(fullPath);
                    context.FileContents[keyFile] = content;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read key file: {File}", keyFile);
                }
            }
        }
        
        // Also read main entry points based on project type
        if (context.ProjectType.Contains("Node.js"))
        {
            await TryReadFileAsync(rootPath, "index.js", context);
            await TryReadFileAsync(rootPath, "app.js", context);
            await TryReadFileAsync(rootPath, "server.js", context);
            await TryReadFileAsync(rootPath, "src/index.js", context);
        }
        else if (context.ProjectType.Contains(".NET"))
        {
            await TryReadFileAsync(rootPath, "Program.cs", context);
            await TryReadFileAsync(rootPath, "Startup.cs", context);
        }
    }
    
    private async Task TryReadFileAsync(string rootPath, string relativePath, RepositoryContext context)
    {
        var fullPath = Path.Combine(rootPath, relativePath);
        if (File.Exists(fullPath) && !context.FileContents.ContainsKey(relativePath))
        {
            try
            {
                var content = await File.ReadAllTextAsync(fullPath);
                context.FileContents[relativePath] = content;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read file: {File}", relativePath);
            }
        }
    }
    
    private async Task<List<string>> DetectAvailableCommandsAsync(string rootPath, string projectType)
    {
        var commands = new List<string>();
        
        if (projectType.Contains("Node.js"))
        {
            var packageJsonPath = Path.Combine(rootPath, "package.json");
            if (File.Exists(packageJsonPath))
            {
                try
                {
                    var packageJson = await File.ReadAllTextAsync(packageJsonPath);
                    var scriptsMatch = System.Text.RegularExpressions.Regex.Match(
                        packageJson, 
                        @"""scripts""\s*:\s*\{([^}]+)\}", 
                        System.Text.RegularExpressions.RegexOptions.Singleline);
                    
                    if (scriptsMatch.Success)
                    {
                        var scripts = scriptsMatch.Groups[1].Value;
                        var scriptNames = System.Text.RegularExpressions.Regex.Matches(scripts, @"""([^""]+)""\s*:");
                        foreach (System.Text.RegularExpressions.Match match in scriptNames)
                        {
                            commands.Add($"npm run {match.Groups[1].Value}");
                        }
                    }
                }
                catch { }
            }
        }
        
        return commands;
    }
}