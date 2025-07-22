# VoiceCode Mac Agent Architecture

## Overview

The VoiceCode Mac Agent is a background service running on macOS that receives code generation commands from the cloud and applies them to local development environments. Built with .NET MAUI for cross-platform compatibility.

## Architecture Components

### 1. Core Agent Service

```csharp
// mac-agent/VoiceCode.MacAgent/Services/AgentService.cs
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Hosting;

namespace VoiceCode.MacAgent.Services
{
    public class AgentService : BackgroundService
    {
        private readonly ServiceBusProcessor _processor;
        private readonly IFileSystemService _fileSystem;
        private readonly IIDEIntegrationService _ideIntegration;
        private readonly INotificationService _notifications;
        private readonly ILogger<AgentService> _logger;

        public AgentService(
            ServiceBusProcessor processor,
            IFileSystemService fileSystem,
            IIDEIntegrationService ideIntegration,
            INotificationService notifications,
            ILogger<AgentService> logger)
        {
            _processor = processor;
            _fileSystem = fileSystem;
            _ideIntegration = ideIntegration;
            _notifications = notifications;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Configure message handling
            _processor.ProcessMessageAsync += HandleMessageAsync;
            _processor.ProcessErrorAsync += HandleErrorAsync;

            // Start processing
            await _processor.StartProcessingAsync(stoppingToken);

            // Keep service running
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        private async Task HandleMessageAsync(ProcessMessageEventArgs args)
        {
            try
            {
                var command = args.Message.Body.ToObjectFromJson<MacAgentCommand>();
                _logger.LogInformation($"Processing command: {command.CommandId}");

                switch (command.Action)
                {
                    case "ApplyChanges":
                        await ApplyFileChangesAsync(command);
                        break;
                    case "OpenInIDE":
                        await OpenInIDEAsync(command);
                        break;
                    case "RunCommand":
                        await RunCommandAsync(command);
                        break;
                    default:
                        _logger.LogWarning($"Unknown action: {command.Action}");
                        break;
                }

                // Complete the message
                await args.CompleteMessageAsync(args.Message);

                // Send notification
                await _notifications.ShowSuccessAsync(
                    "VoiceCode",
                    $"Successfully applied changes from {command.Action}"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
                await _notifications.ShowErrorAsync(
                    "VoiceCode",
                    $"Failed to apply changes: {ex.Message}"
                );
                
                // Dead letter the message after retries
                if (args.Message.DeliveryCount >= 3)
                {
                    await args.DeadLetterMessageAsync(args.Message, ex.Message);
                }
                else
                {
                    await args.AbandonMessageAsync(args.Message);
                }
            }
        }

        private async Task ApplyFileChangesAsync(MacAgentCommand command)
        {
            foreach (var file in command.Files)
            {
                var fullPath = Path.Combine(command.Repository, file.Path);

                switch (file.Action)
                {
                    case FileAction.Create:
                        await _fileSystem.CreateFileAsync(fullPath, file.Content);
                        break;
                    case FileAction.Modify:
                        await _fileSystem.UpdateFileAsync(fullPath, file.Content);
                        break;
                    case FileAction.Delete:
                        await _fileSystem.DeleteFileAsync(fullPath);
                        break;
                }

                _logger.LogInformation($"{file.Action} file: {fullPath}");
            }

            // Optionally commit changes
            if (command.AutoCommit)
            {
                await CommitChangesAsync(command.Repository, command.CommitMessage);
            }
        }

        private async Task OpenInIDEAsync(MacAgentCommand command)
        {
            var ide = DetectPreferredIDE();
            
            foreach (var file in command.Files)
            {
                var fullPath = Path.Combine(command.Repository, file.Path);
                await _ideIntegration.OpenFileAsync(ide, fullPath, file.LineNumber);
            }
        }

        private IDEType DetectPreferredIDE()
        {
            // Check which IDEs are installed and running
            if (_ideIntegration.IsIDERunning(IDEType.VSCode))
                return IDEType.VSCode;
            if (_ideIntegration.IsIDERunning(IDEType.Cursor))
                return IDEType.Cursor;
            if (_ideIntegration.IsIDERunning(IDEType.VisualStudio))
                return IDEType.VisualStudio;
            
            // Default to VS Code
            return IDEType.VSCode;
        }
    }
}
```

### 2. File System Service

```csharp
// mac-agent/VoiceCode.MacAgent/Services/FileSystemService.cs
namespace VoiceCode.MacAgent.Services
{
    public interface IFileSystemService
    {
        Task<string> ReadFileAsync(string path);
        Task CreateFileAsync(string path, string content);
        Task UpdateFileAsync(string path, string content);
        Task DeleteFileAsync(string path);
        Task<bool> FileExistsAsync(string path);
        Task CreateBackupAsync(string path);
    }

    public class FileSystemService : IFileSystemService
    {
        private readonly ILogger<FileSystemService> _logger;
        private readonly string _backupDirectory;

        public FileSystemService(ILogger<FileSystemService> logger)
        {
            _logger = logger;
            _backupDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".voicecode",
                "backups"
            );
            Directory.CreateDirectory(_backupDirectory);
        }

        public async Task<string> ReadFileAsync(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"File not found: {path}");

            return await File.ReadAllTextAsync(path);
        }

        public async Task CreateFileAsync(string path, string content)
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Check if file already exists
            if (File.Exists(path))
            {
                await CreateBackupAsync(path);
            }

            await File.WriteAllTextAsync(path, content);
            _logger.LogInformation($"Created file: {path}");
        }

        public async Task UpdateFileAsync(string path, string content)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"File not found: {path}");

            // Create backup before updating
            await CreateBackupAsync(path);

            await File.WriteAllTextAsync(path, content);
            _logger.LogInformation($"Updated file: {path}");
        }

        public async Task DeleteFileAsync(string path)
        {
            if (!File.Exists(path))
                return;

            // Create backup before deleting
            await CreateBackupAsync(path);

            File.Delete(path);
            _logger.LogInformation($"Deleted file: {path}");
        }

        public Task<bool> FileExistsAsync(string path)
        {
            return Task.FromResult(File.Exists(path));
        }

        public async Task CreateBackupAsync(string path)
        {
            if (!File.Exists(path))
                return;

            var fileName = Path.GetFileName(path);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var backupPath = Path.Combine(_backupDirectory, $"{fileName}.{timestamp}.backup");

            await File.CopyAsync(path, backupPath);
            _logger.LogDebug($"Created backup: {backupPath}");

            // Clean old backups (keep last 50)
            await CleanOldBackupsAsync();
        }

        private async Task CleanOldBackupsAsync()
        {
            var backups = Directory.GetFiles(_backupDirectory, "*.backup")
                .OrderByDescending(f => File.GetCreationTimeUtc(f))
                .Skip(50)
                .ToList();

            foreach (var backup in backups)
            {
                File.Delete(backup);
            }
        }
    }
}
```

### 3. IDE Integration Service

```csharp
// mac-agent/VoiceCode.MacAgent/Services/IDEIntegrationService.cs
using System.Diagnostics;

namespace VoiceCode.MacAgent.Services
{
    public enum IDEType
    {
        VSCode,
        Cursor,
        VisualStudio,
        Xcode
    }

    public interface IIDEIntegrationService
    {
        Task OpenFileAsync(IDEType ide, string filePath, int? lineNumber = null);
        bool IsIDERunning(IDEType ide);
        Task<bool> IsIDEInstalledAsync(IDEType ide);
    }

    public class IDEIntegrationService : IIDEIntegrationService
    {
        private readonly ILogger<IDEIntegrationService> _logger;

        private readonly Dictionary<IDEType, IDEInfo> _ideInfo = new()
        {
            [IDEType.VSCode] = new IDEInfo
            {
                Name = "Visual Studio Code",
                ProcessName = "Code",
                CommandPath = "/usr/local/bin/code",
                OpenCommand = "code -g {0}:{1}",
                BundleId = "com.microsoft.VSCode"
            },
            [IDEType.Cursor] = new IDEInfo
            {
                Name = "Cursor",
                ProcessName = "Cursor",
                CommandPath = "/usr/local/bin/cursor",
                OpenCommand = "cursor -g {0}:{1}",
                BundleId = "com.todesktop.230313mzl4w4u92"
            },
            [IDEType.VisualStudio] = new IDEInfo
            {
                Name = "Visual Studio for Mac",
                ProcessName = "VisualStudio",
                CommandPath = "/usr/local/bin/vstool",
                OpenCommand = "vstool open {0}",
                BundleId = "com.microsoft.visual-studio"
            },
            [IDEType.Xcode] = new IDEInfo
            {
                Name = "Xcode",
                ProcessName = "Xcode",
                CommandPath = "/usr/bin/xed",
                OpenCommand = "xed --line {1} {0}",
                BundleId = "com.apple.dt.Xcode"
            }
        };

        public IDEIntegrationService(ILogger<IDEIntegrationService> logger)
        {
            _logger = logger;
        }

        public async Task OpenFileAsync(IDEType ide, string filePath, int? lineNumber = null)
        {
            if (!_ideInfo.TryGetValue(ide, out var info))
            {
                throw new NotSupportedException($"IDE {ide} is not supported");
            }

            try
            {
                string command;
                if (lineNumber.HasValue && info.OpenCommand.Contains("{1}"))
                {
                    command = string.Format(info.OpenCommand, filePath, lineNumber.Value);
                }
                else
                {
                    command = string.Format(info.OpenCommand.Replace(":{1}", ""), filePath);
                }

                // Use AppleScript for better macOS integration
                var appleScript = $@"
                    tell application ""{info.Name}""
                        activate
                    end tell
                ";

                await RunAppleScriptAsync(appleScript);
                await RunCommandAsync(command);

                _logger.LogInformation($"Opened {filePath} in {info.Name}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to open file in {info.Name}");
                throw;
            }
        }

        public bool IsIDERunning(IDEType ide)
        {
            if (!_ideInfo.TryGetValue(ide, out var info))
                return false;

            try
            {
                var processes = Process.GetProcessesByName(info.ProcessName);
                return processes.Any();
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> IsIDEInstalledAsync(IDEType ide)
        {
            if (!_ideInfo.TryGetValue(ide, out var info))
                return false;

            // Check if command exists
            if (File.Exists(info.CommandPath))
                return true;

            // Check using macOS bundle ID
            var checkScript = $@"
                tell application ""Finder""
                    try
                        application id ""{info.BundleId}""
                        return ""installed""
                    on error
                        return ""not installed""
                    end try
                end tell
            ";

            var result = await RunAppleScriptAsync(checkScript);
            return result.Contains("installed") && !result.Contains("not");
        }

        private async Task<string> RunAppleScriptAsync(string script)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/osascript",
                    Arguments = $"-e '{script}'",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return output;
        }

        private async Task RunCommandAsync(string command)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \"{command}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();
        }

        private class IDEInfo
        {
            public string Name { get; set; }
            public string ProcessName { get; set; }
            public string CommandPath { get; set; }
            public string OpenCommand { get; set; }
            public string BundleId { get; set; }
        }
    }
}
```

### 4. Git Integration

```csharp
// mac-agent/VoiceCode.MacAgent/Services/GitService.cs
using LibGit2Sharp;

namespace VoiceCode.MacAgent.Services
{
    public interface IGitService
    {
        Task<bool> IsGitRepositoryAsync(string path);
        Task CommitChangesAsync(string repoPath, string message);
        Task<string> GetCurrentBranchAsync(string repoPath);
        Task CreateBranchAsync(string repoPath, string branchName);
        Task<IEnumerable<string>> GetModifiedFilesAsync(string repoPath);
    }

    public class GitService : IGitService
    {
        private readonly ILogger<GitService> _logger;

        public GitService(ILogger<GitService> logger)
        {
            _logger = logger;
        }

        public Task<bool> IsGitRepositoryAsync(string path)
        {
            return Task.FromResult(Repository.IsValid(path));
        }

        public async Task CommitChangesAsync(string repoPath, string message)
        {
            using var repo = new Repository(repoPath);
            
            // Stage all modified files
            Commands.Stage(repo, "*");

            // Create signature
            var signature = new Signature(
                "VoiceCode Agent",
                "agent@voicecode.ai",
                DateTimeOffset.Now
            );

            // Commit
            var commit = repo.Commit(
                message ?? "Changes applied by VoiceCode",
                signature,
                signature
            );

            _logger.LogInformation($"Created commit: {commit.Id.Sha}");
        }

        public Task<string> GetCurrentBranchAsync(string repoPath)
        {
            using var repo = new Repository(repoPath);
            return Task.FromResult(repo.Head.FriendlyName);
        }

        public async Task CreateBranchAsync(string repoPath, string branchName)
        {
            using var repo = new Repository(repoPath);
            
            // Create and checkout new branch
            var branch = repo.CreateBranch(branchName);
            Commands.Checkout(repo, branch);

            _logger.LogInformation($"Created and checked out branch: {branchName}");
        }

        public Task<IEnumerable<string>> GetModifiedFilesAsync(string repoPath)
        {
            using var repo = new Repository(repoPath);
            
            var modifiedFiles = repo.RetrieveStatus()
                .Where(s => s.State != FileStatus.Ignored && s.State != FileStatus.Unaltered)
                .Select(s => s.FilePath);

            return Task.FromResult(modifiedFiles);
        }
    }
}
```

### 5. Security and Sandboxing

```csharp
// mac-agent/VoiceCode.MacAgent/Security/SecurityService.cs
namespace VoiceCode.MacAgent.Security
{
    public interface ISecurityService
    {
        Task<bool> ValidateCommandAsync(MacAgentCommand command);
        Task<bool> IsPathAllowedAsync(string path);
        void ConfigureAllowedPaths(IEnumerable<string> paths);
    }

    public class SecurityService : ISecurityService
    {
        private readonly HashSet<string> _allowedPaths;
        private readonly HashSet<string> _blockedPatterns;
        private readonly ILogger<SecurityService> _logger;

        public SecurityService(IConfiguration configuration, ILogger<SecurityService> logger)
        {
            _logger = logger;
            _allowedPaths = new HashSet<string>();
            _blockedPatterns = new HashSet<string>
            {
                "/System",
                "/Library",
                "/private",
                "~/.ssh",
                "~/.aws",
                "~/.azure",
                ".git/config",
                ".env",
                "secrets",
                "credentials"
            };

            // Load allowed paths from configuration
            var configPaths = configuration.GetSection("Security:AllowedPaths").Get<string[]>();
            if (configPaths != null)
            {
                foreach (var path in configPaths)
                {
                    _allowedPaths.Add(ExpandPath(path));
                }
            }
        }

        public async Task<bool> ValidateCommandAsync(MacAgentCommand command)
        {
            // Validate user authorization
            if (string.IsNullOrEmpty(command.UserId))
            {
                _logger.LogWarning("Command rejected: Missing user ID");
                return false;
            }

            // Validate repository path
            if (!await IsPathAllowedAsync(command.Repository))
            {
                _logger.LogWarning($"Command rejected: Repository path not allowed: {command.Repository}");
                return false;
            }

            // Validate file paths
            foreach (var file in command.Files)
            {
                var fullPath = Path.Combine(command.Repository, file.Path);
                if (!await IsPathAllowedAsync(fullPath))
                {
                    _logger.LogWarning($"Command rejected: File path not allowed: {fullPath}");
                    return false;
                }

                // Validate file content
                if (ContainsSensitiveData(file.Content))
                {
                    _logger.LogWarning($"Command rejected: File contains sensitive data: {file.Path}");
                    return false;
                }
            }

            return true;
        }

        public Task<bool> IsPathAllowedAsync(string path)
        {
            var expandedPath = ExpandPath(path);

            // Check if path is in blocked patterns
            foreach (var pattern in _blockedPatterns)
            {
                if (expandedPath.Contains(ExpandPath(pattern)))
                {
                    return Task.FromResult(false);
                }
            }

            // Check if path is within allowed paths
            foreach (var allowedPath in _allowedPaths)
            {
                if (expandedPath.StartsWith(allowedPath))
                {
                    return Task.FromResult(true);
                }
            }

            // Default to deny
            return Task.FromResult(false);
        }

        public void ConfigureAllowedPaths(IEnumerable<string> paths)
        {
            _allowedPaths.Clear();
            foreach (var path in paths)
            {
                _allowedPaths.Add(ExpandPath(path));
            }
        }

        private string ExpandPath(string path)
        {
            if (path.StartsWith("~"))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return path.Replace("~", home);
            }
            return Path.GetFullPath(path);
        }

        private bool ContainsSensitiveData(string content)
        {
            // Check for common patterns of sensitive data
            var sensitivePatterns = new[]
            {
                @"-----BEGIN (RSA|DSA|EC|OPENSSH) PRIVATE KEY-----",
                @"AIza[0-9A-Za-z\-_]{35}", // Google API key
                @"sk_live_[0-9a-zA-Z]{24}", // Stripe
                @"xox[baprs]-[0-9a-zA-Z]{10,48}", // Slack
                @"ghp_[0-9a-zA-Z]{36}", // GitHub personal access token
            };

            foreach (var pattern in sensitivePatterns)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(content, pattern))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
```

### 6. Configuration and Startup

```csharp
// mac-agent/VoiceCode.MacAgent/Program.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Azure.Messaging.ServiceBus;
using Azure.Identity;

namespace VoiceCode.MacAgent
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var host = CreateHostBuilder(args).Build();
            await host.RunAsync();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, config) =>
                {
                    config
                        .SetBasePath(Directory.GetCurrentDirectory())
                        .AddJsonFile("appsettings.json", optional: false)
                        .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true)
                        .AddUserSecrets<Program>(optional: true)
                        .AddEnvironmentVariables("VOICECODE_")
                        .AddCommandLine(args);
                })
                .ConfigureServices((context, services) =>
                {
                    // Add Azure clients
                    services.AddSingleton<ServiceBusClient>(provider =>
                    {
                        var connectionString = context.Configuration.GetConnectionString("ServiceBus");
                        return new ServiceBusClient(connectionString);
                    });

                    services.AddSingleton<ServiceBusProcessor>(provider =>
                    {
                        var client = provider.GetRequiredService<ServiceBusClient>();
                        return client.CreateProcessor(
                            "mac-agent-queue",
                            new ServiceBusProcessorOptions
                            {
                                AutoCompleteMessages = false,
                                MaxConcurrentCalls = 1, // Process one at a time
                                PrefetchCount = 0
                            }
                        );
                    });

                    // Add services
                    services.AddSingleton<IFileSystemService, FileSystemService>();
                    services.AddSingleton<IIDEIntegrationService, IDEIntegrationService>();
                    services.AddSingleton<IGitService, GitService>();
                    services.AddSingleton<ISecurityService, SecurityService>();
                    services.AddSingleton<INotificationService, NotificationService>();

                    // Add hosted service
                    services.AddHostedService<AgentService>();

                    // Add logging
                    services.AddLogging(builder =>
                    {
                        builder
                            .AddConsole()
                            .AddFile("logs/voicecode-agent-{Date}.log");
                    });
                })
                .UseContentRoot(Directory.GetCurrentDirectory());
    }
}
```

### 7. macOS App Bundle

```xml
<!-- mac-agent/VoiceCode.MacAgent/Platforms/MacCatalyst/Info.plist -->
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleIdentifier</key>
    <string>com.voicecode.agent</string>
    <key>CFBundleName</key>
    <string>VoiceCode Agent</string>
    <key>CFBundleVersion</key>
    <string>1.0.0</string>
    <key>LSUIElement</key>
    <true/> <!-- Run as background app -->
    <key>NSAppleEventsUsageDescription</key>
    <string>VoiceCode needs to send commands to your development tools.</string>
    <key>NSDesktopFolderUsageDescription</key>
    <string>VoiceCode needs access to your project folders to apply code changes.</string>
</dict>
</plist>
```

## Deployment

### Installation Script

```bash
#!/bin/bash
# install-voicecode-agent.sh

echo "Installing VoiceCode Mac Agent..."

# Download latest release
curl -L https://github.com/voicecode/mac-agent/releases/latest/download/VoiceCode.MacAgent.pkg -o /tmp/VoiceCode.MacAgent.pkg

# Install package
sudo installer -pkg /tmp/VoiceCode.MacAgent.pkg -target /

# Configure launch agent
cat > ~/Library/LaunchAgents/com.voicecode.agent.plist << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.voicecode.agent</string>
    <key>ProgramArguments</key>
    <array>
        <string>/Applications/VoiceCode Agent.app/Contents/MacOS/VoiceCode.MacAgent</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
    <key>StandardErrorPath</key>
    <string>/tmp/voicecode-agent.err</string>
    <key>StandardOutPath</key>
    <string>/tmp/voicecode-agent.out</string>
</dict>
</plist>
EOF

# Load launch agent
launchctl load ~/Library/LaunchAgents/com.voicecode.agent.plist

echo "VoiceCode Agent installed successfully!"
```

## Security Considerations

1. **Code Signing**: The Mac agent must be properly code signed with a Developer ID certificate
2. **Notarization**: Submit to Apple for notarization to avoid Gatekeeper warnings
3. **Sandboxing**: Limit file system access to specific directories
4. **Authentication**: Validate all commands come from authorized users
5. **Encryption**: All communication with Azure Service Bus is encrypted
6. **Backup**: Automatic backup before any file modifications

## Monitoring

- Local logs in `~/Library/Logs/VoiceCode/`
- Telemetry sent to Application Insights
- Health endpoint for monitoring agent status
- Automatic crash reporting