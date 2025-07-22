# VoiceCode Services - C# Implementation

## Overview

All VoiceCode backend services are implemented as ASP.NET Core Web APIs hosted on Azure Web Apps. This provides a cost-effective, scalable solution with full .NET 8 capabilities.

## Common Service Configuration

### Base Service Template

```csharp
// Program.cs (Common for all services)
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

var builder = WebApplication.CreateBuilder(args);

// Add Azure Key Vault
var keyVaultEndpoint = new Uri($"https://{builder.Configuration["KeyVaultName"]}.vault.azure.net/");
builder.Configuration.AddAzureKeyVault(keyVaultEndpoint, new DefaultAzureCredential());

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddApplicationInsightsTelemetry();
builder.Services.AddHealthChecks();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowMobileApp",
        builder => builder
            .WithOrigins(configuration["AllowedOrigins"])
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});

// Add authentication
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.Authority = builder.Configuration["AzureAd:Authority"];
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudience = builder.Configuration["AzureAd:Audience"]
        };
    });

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowMobileApp");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
```

### Shared Models

```csharp
// shared/VoiceCode.Common/Models/BaseModels.cs
namespace VoiceCode.Common.Models
{
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public T Data { get; set; }
        public string Error { get; set; }
        public string RequestId { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class ProcessingRequest
    {
        public string UserId { get; set; }
        public string SessionId { get; set; }
        public Dictionary<string, string> Metadata { get; set; }
    }

    public class ProcessingResult
    {
        public string Id { get; set; }
        public string Status { get; set; }
        public object Result { get; set; }
        public DateTime ProcessedAt { get; set; }
    }
}
```

## 1. Speech-to-Text Service

```csharp
// services/stt-service/Services/SpeechService.cs
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

namespace VoiceCode.STTService.Services
{
    public interface ISpeechService
    {
        Task<TranscriptionResult> TranscribeAsync(byte[] audioData, string language = "en-US");
    }

    public class SpeechService : ISpeechService
    {
        private readonly SpeechConfig _speechConfig;
        private readonly ILogger<SpeechService> _logger;

        public SpeechService(IConfiguration configuration, ILogger<SpeechService> logger)
        {
            _logger = logger;
            _speechConfig = SpeechConfig.FromSubscription(
                configuration["AzureSpeech:Key"],
                configuration["AzureSpeech:Region"]
            );
        }

        public async Task<TranscriptionResult> TranscribeAsync(byte[] audioData, string language = "en-US")
        {
            _speechConfig.SpeechRecognitionLanguage = language;

            using var audioStream = new MemoryStream(audioData);
            using var audioConfig = AudioConfig.FromStreamInput(audioStream);
            using var recognizer = new SpeechRecognizer(_speechConfig, audioConfig);

            // Enable dictation mode for better transcription
            recognizer.Properties.SetProperty(
                PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs, 
                "3000"
            );

            var result = await recognizer.RecognizeOnceAsync();

            if (result.Reason == ResultReason.RecognizedSpeech)
            {
                return new TranscriptionResult
                {
                    Success = true,
                    Transcript = result.Text,
                    Confidence = ExtractConfidence(result),
                    Language = language,
                    DurationMs = result.Duration.TotalMilliseconds,
                    AlternativeTranscripts = GetAlternatives(result)
                };
            }

            _logger.LogWarning($"Recognition failed: {result.Reason}");
            throw new InvalidOperationException($"Speech recognition failed: {result.Reason}");
        }

        private double ExtractConfidence(SpeechRecognitionResult result)
        {
            try
            {
                var json = result.Properties.GetProperty(PropertyId.SpeechServiceResponse_JsonResult);
                var response = JsonSerializer.Deserialize<SpeechServiceResponse>(json);
                return response?.NBest?.FirstOrDefault()?.Confidence ?? 0.0;
            }
            catch
            {
                return 0.85; // Default confidence
            }
        }

        private List<string> GetAlternatives(SpeechRecognitionResult result)
        {
            try
            {
                var json = result.Properties.GetProperty(PropertyId.SpeechServiceResponse_JsonResult);
                var response = JsonSerializer.Deserialize<SpeechServiceResponse>(json);
                return response?.NBest?.Skip(1).Take(2).Select(n => n.Display).ToList() ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }
    }

    public class TranscriptionResult
    {
        public bool Success { get; set; }
        public string Transcript { get; set; }
        public double Confidence { get; set; }
        public string Language { get; set; }
        public double DurationMs { get; set; }
        public List<string> AlternativeTranscripts { get; set; }
    }
}
```

## 2. Claude Integration Service

```csharp
// services/claude-service/Services/ClaudeService.cs
using Polly;
using Polly.Extensions.Http;

namespace VoiceCode.ClaudeService.Services
{
    public interface IClaudeService
    {
        Task<CodeGenerationResponse> GenerateCodeAsync(CodeGenerationRequest request);
    }

    public class ClaudeService : IClaudeService
    {
        private readonly HttpClient _httpClient;
        private readonly IDistributedCache _cache;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ClaudeService> _logger;
        private readonly IAsyncPolicy<HttpResponseMessage> _retryPolicy;

        public ClaudeService(
            HttpClient httpClient,
            IDistributedCache cache,
            IConfiguration configuration,
            ILogger<ClaudeService> logger)
        {
            _httpClient = httpClient;
            _cache = cache;
            _configuration = configuration;
            _logger = logger;

            // Configure retry policy with exponential backoff
            _retryPolicy = HttpPolicyExtensions
                .HandleTransientHttpError()
                .OrResult(msg => !msg.IsSuccessStatusCode)
                .WaitAndRetryAsync(
                    3,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (outcome, timespan, retryCount, context) =>
                    {
                        _logger.LogWarning($"Retry {retryCount} after {timespan} seconds");
                    });

            // Configure HTTP client
            _httpClient.BaseAddress = new Uri("https://api.anthropic.com/v1/");
            _httpClient.DefaultRequestHeaders.Add("x-api-key", configuration["Claude:ApiKey"]);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        }

        public async Task<CodeGenerationResponse> GenerateCodeAsync(CodeGenerationRequest request)
        {
            // Check cache
            var cacheKey = GenerateCacheKey(request);
            var cachedResponse = await _cache.GetAsync(cacheKey);
            if (cachedResponse != null)
            {
                return JsonSerializer.Deserialize<CodeGenerationResponse>(cachedResponse);
            }

            // Build enhanced prompt
            var prompt = await BuildEnhancedPrompt(request);

            // Validate token budget
            var estimatedTokens = EstimateTokens(prompt);
            if (estimatedTokens > request.MaxTokens)
            {
                throw new InvalidOperationException(
                    $"Estimated tokens ({estimatedTokens}) exceeds budget ({request.MaxTokens})"
                );
            }

            // Call Claude API with retry policy
            var apiRequest = new
            {
                model = _configuration["Claude:Model"] ?? "claude-3-opus-20240229",
                max_tokens = request.MaxTokens,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = prompt
                    }
                },
                temperature = 0.2,
                system = "You are an expert programmer who writes clean, efficient, and well-documented code."
            };

            var response = await _retryPolicy.ExecuteAsync(async () =>
                await _httpClient.PostAsJsonAsync("messages", apiRequest)
            );

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var claudeResponse = JsonSerializer.Deserialize<ClaudeAPIResponse>(content);

            var result = ProcessClaudeResponse(claudeResponse, request);

            // Cache the result
            await _cache.SetAsync(
                cacheKey,
                JsonSerializer.SerializeToUtf8Bytes(result),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
                }
            );

            return result;
        }

        private async Task<string> BuildEnhancedPrompt(CodeGenerationRequest request)
        {
            var promptBuilder = new StringBuilder();

            // Add context
            if (!string.IsNullOrEmpty(request.Context))
            {
                promptBuilder.AppendLine($"Context: {request.Context}");
                promptBuilder.AppendLine();
            }

            // Add main instruction
            promptBuilder.AppendLine($"Task: {request.Instruction}");
            promptBuilder.AppendLine();

            // Add language-specific guidelines
            promptBuilder.AppendLine($"Programming Language: {request.Language}");
            promptBuilder.AppendLine($"Coding Style: {request.CodingStyle}");
            promptBuilder.AppendLine();

            // Add output format instructions
            promptBuilder.AppendLine("Please provide:");
            promptBuilder.AppendLine("1. Complete, working code implementation");
            promptBuilder.AppendLine("2. Clear comments explaining complex logic");
            promptBuilder.AppendLine("3. Any necessary imports or dependencies");
            promptBuilder.AppendLine("4. Example usage if applicable");

            return promptBuilder.ToString();
        }

        private CodeGenerationResponse ProcessClaudeResponse(
            ClaudeAPIResponse apiResponse, 
            CodeGenerationRequest request)
        {
            var content = apiResponse.Content[0].Text;

            return new CodeGenerationResponse
            {
                Id = Guid.NewGuid().ToString(),
                Code = ExtractCodeBlocks(content),
                Explanation = ExtractExplanation(content),
                Confidence = CalculateConfidence(apiResponse, request),
                SuggestedFiles = ExtractFileNames(content),
                Dependencies = ExtractDependencies(content, request.Language),
                Warnings = ExtractWarnings(content),
                CreatedAt = DateTime.UtcNow
            };
        }

        private List<CodeBlock> ExtractCodeBlocks(string content)
        {
            var codeBlocks = new List<CodeBlock>();
            var pattern = @"```(?<lang>\w*)\n(?<code>.*?)```";
            var matches = Regex.Matches(content, pattern, RegexOptions.Singleline);

            foreach (Match match in matches)
            {
                var language = match.Groups["lang"].Value;
                var code = match.Groups["code"].Value.Trim();

                codeBlocks.Add(new CodeBlock
                {
                    Language = string.IsNullOrEmpty(language) ? "plaintext" : language,
                    Content = code,
                    LineCount = code.Split('\n').Length,
                    EstimatedComplexity = EstimateCodeComplexity(code)
                });
            }

            return codeBlocks;
        }

        private int EstimateTokens(string text)
        {
            // More accurate token estimation
            // Average: ~1 token per 4 characters for code, ~1 token per 5 for natural language
            var codeRatio = CountCodeCharacters(text) / (double)text.Length;
            var avgCharsPerToken = 4 * codeRatio + 5 * (1 - codeRatio);
            return (int)(text.Length / avgCharsPerToken);
        }

        private int CountCodeCharacters(string text)
        {
            var codePattern = @"```.*?```";
            var matches = Regex.Matches(text, codePattern, RegexOptions.Singleline);
            return matches.Sum(m => m.Length);
        }
    }
}
```

## 3. Prompt Router Service

```csharp
// services/prompt-router/Services/PromptRouterService.cs
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Cosmos;

namespace VoiceCode.PromptRouter.Services
{
    public interface IPromptRouterService
    {
        Task<RoutingResult> RouteRequestAsync(RoutingRequest request);
    }

    public class PromptRouterService : IPromptRouterService
    {
        private readonly ServiceBusClient _serviceBusClient;
        private readonly CosmosClient _cosmosClient;
        private readonly IIntentClassifier _intentClassifier;
        private readonly IContextManager _contextManager;
        private readonly ILogger<PromptRouterService> _logger;

        public PromptRouterService(
            ServiceBusClient serviceBusClient,
            CosmosClient cosmosClient,
            IIntentClassifier intentClassifier,
            IContextManager contextManager,
            ILogger<PromptRouterService> logger)
        {
            _serviceBusClient = serviceBusClient;
            _cosmosClient = cosmosClient;
            _intentClassifier = intentClassifier;
            _contextManager = contextManager;
            _logger = logger;
        }

        public async Task<RoutingResult> RouteRequestAsync(RoutingRequest request)
        {
            // Classify intent
            var intent = await _intentClassifier.ClassifyAsync(request.Transcript);
            _logger.LogInformation($"Classified intent: {intent.Type} (confidence: {intent.Confidence})");

            // Retrieve context
            var context = await _contextManager.GetContextAsync(request.UserId, request.SessionId);
            
            // Build enhanced prompt
            var enhancedPrompt = await BuildEnhancedPrompt(request, intent, context);

            // Determine routing destination
            var route = DetermineRoute(intent);

            // Send to appropriate service via Service Bus
            var sender = _serviceBusClient.CreateSender(route.QueueName);
            
            var message = new ServiceBusMessage
            {
                Body = BinaryData.FromObjectAsJson(new ProcessingMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    UserId = request.UserId,
                    SessionId = request.SessionId,
                    Intent = intent,
                    Prompt = enhancedPrompt,
                    Context = context,
                    Timestamp = DateTime.UtcNow
                }),
                Subject = route.Subject,
                SessionId = request.SessionId,
                TimeToLive = TimeSpan.FromMinutes(5)
            };

            // Add custom properties for routing
            message.ApplicationProperties.Add("Intent", intent.Type);
            message.ApplicationProperties.Add("Priority", DeterminePriority(intent));
            message.ApplicationProperties.Add("UserId", request.UserId);

            await sender.SendMessageAsync(message);

            // Update context with this interaction
            await _contextManager.UpdateContextAsync(request.UserId, request.SessionId, new ContextEntry
            {
                Transcript = request.Transcript,
                Intent = intent,
                Timestamp = DateTime.UtcNow
            });

            return new RoutingResult
            {
                Success = true,
                Route = route,
                Intent = intent,
                MessageId = message.MessageId,
                EnhancedPrompt = enhancedPrompt
            };
        }

        private async Task<EnhancedPrompt> BuildEnhancedPrompt(
            RoutingRequest request, 
            Intent intent, 
            UserContext context)
        {
            var promptBuilder = new PromptBuilder();

            // Add base instruction
            promptBuilder.AddInstruction(request.Transcript);

            // Add relevant context
            if (context.RecentInteractions.Any())
            {
                var relevantContext = context.RecentInteractions
                    .Where(i => IsRelevantToIntent(i, intent))
                    .Take(3)
                    .ToList();

                if (relevantContext.Any())
                {
                    promptBuilder.AddContext("Recent related interactions:", relevantContext);
                }
            }

            // Add user preferences
            if (context.Preferences != null)
            {
                promptBuilder.AddPreferences(context.Preferences);
            }

            // Add intent-specific enhancements
            switch (intent.Type)
            {
                case "code_generation":
                    promptBuilder.AddCodeGenerationGuidelines();
                    break;
                case "bug_fix":
                    promptBuilder.AddBugFixGuidelines();
                    break;
                case "refactor":
                    promptBuilder.AddRefactoringGuidelines();
                    break;
                case "explain":
                    promptBuilder.AddExplanationGuidelines();
                    break;
            }

            return promptBuilder.Build();
        }

        private Route DetermineRoute(Intent intent)
        {
            var routeMap = new Dictionary<string, Route>
            {
                ["code_generation"] = new Route { QueueName = "claude-processing", Subject = "generate" },
                ["bug_fix"] = new Route { QueueName = "claude-processing", Subject = "fix" },
                ["refactor"] = new Route { QueueName = "claude-processing", Subject = "refactor" },
                ["explain"] = new Route { QueueName = "claude-processing", Subject = "explain" },
                ["file_operation"] = new Route { QueueName = "file-processing", Subject = "file" },
                ["search"] = new Route { QueueName = "search-processing", Subject = "search" },
                ["unknown"] = new Route { QueueName = "claude-processing", Subject = "general" }
            };

            return routeMap.GetValueOrDefault(intent.Type, routeMap["unknown"]);
        }

        private string DeterminePriority(Intent intent)
        {
            // Higher confidence and certain intent types get higher priority
            if (intent.Confidence > 0.9 || intent.Type == "bug_fix")
                return "high";
            else if (intent.Confidence > 0.7)
                return "normal";
            else
                return "low";
        }
    }

    // Intent Classifier using Azure Cognitive Services
    public class IntentClassifier : IIntentClassifier
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public IntentClassifier(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;
        }

        public async Task<Intent> ClassifyAsync(string text)
        {
            // Use Azure Language Understanding (LUIS) or custom classifier
            var request = new
            {
                documents = new[]
                {
                    new { id = "1", text = text }
                }
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_configuration["CognitiveServices:Endpoint"]}/text/analytics/v3.1/sentiment",
                request
            );

            // Process response and map to intent
            // This is a simplified example - real implementation would use
            // a trained model for intent classification
            
            return ClassifyByKeywords(text);
        }

        private Intent ClassifyByKeywords(string text)
        {
            var lowerText = text.ToLower();
            
            if (ContainsAny(lowerText, "create", "generate", "make", "build", "write"))
                return new Intent { Type = "code_generation", Confidence = 0.85 };
            
            if (ContainsAny(lowerText, "fix", "bug", "error", "issue", "problem"))
                return new Intent { Type = "bug_fix", Confidence = 0.9 };
            
            if (ContainsAny(lowerText, "refactor", "improve", "optimize", "clean"))
                return new Intent { Type = "refactor", Confidence = 0.8 };
            
            if (ContainsAny(lowerText, "explain", "what", "how", "why", "understand"))
                return new Intent { Type = "explain", Confidence = 0.85 };
            
            if (ContainsAny(lowerText, "file", "save", "open", "delete"))
                return new Intent { Type = "file_operation", Confidence = 0.9 };
            
            if (ContainsAny(lowerText, "search", "find", "look for", "where"))
                return new Intent { Type = "search", Confidence = 0.85 };
            
            return new Intent { Type = "unknown", Confidence = 0.5 };
        }

        private bool ContainsAny(string text, params string[] keywords)
        {
            return keywords.Any(keyword => text.Contains(keyword));
        }
    }
}
```

## 4. Code Generator Service

```csharp
// services/code-generator/Services/CodeGeneratorService.cs
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;

namespace VoiceCode.CodeGenerator.Services
{
    public interface ICodeGeneratorService
    {
        Task<GeneratedFiles> GenerateFilesAsync(CodeGenerationInput input);
    }

    public class CodeGeneratorService : ICodeGeneratorService
    {
        private readonly IFileSystemService _fileSystem;
        private readonly ICodeFormatter _formatter;
        private readonly ISyntaxValidator _validator;
        private readonly ILogger<CodeGeneratorService> _logger;

        public CodeGeneratorService(
            IFileSystemService fileSystem,
            ICodeFormatter formatter,
            ISyntaxValidator validator,
            ILogger<CodeGeneratorService> logger)
        {
            _fileSystem = fileSystem;
            _formatter = formatter;
            _validator = validator;
            _logger = logger;
        }

        public async Task<GeneratedFiles> GenerateFilesAsync(CodeGenerationInput input)
        {
            var files = new List<FileChange>();
            var errors = new List<string>();

            foreach (var codeBlock in input.CodeBlocks)
            {
                try
                {
                    // Determine file path
                    var filePath = DetermineFilePath(codeBlock, input.TargetDirectory);

                    // Format code based on language
                    var formattedCode = await _formatter.FormatAsync(
                        codeBlock.Content, 
                        codeBlock.Language
                    );

                    // Validate syntax
                    var validation = await _validator.ValidateAsync(
                        formattedCode, 
                        codeBlock.Language
                    );

                    if (!validation.IsValid)
                    {
                        errors.Add($"Syntax error in {filePath}: {validation.Error}");
                        continue;
                    }

                    // Check if file exists
                    var existingContent = await _fileSystem.ReadFileAsync(filePath);

                    if (existingContent != null)
                    {
                        // Generate diff
                        var diff = GenerateDiff(existingContent, formattedCode);
                        
                        files.Add(new FileChange
                        {
                            Path = filePath,
                            Action = FileAction.Modify,
                            Content = formattedCode,
                            OriginalContent = existingContent,
                            Diff = diff,
                            ConflictMarkers = DetectConflicts(diff)
                        });
                    }
                    else
                    {
                        files.Add(new FileChange
                        {
                            Path = filePath,
                            Action = FileAction.Create,
                            Content = formattedCode
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing code block");
                    errors.Add($"Error processing code block: {ex.Message}");
                }
            }

            return new GeneratedFiles
            {
                Files = files,
                Summary = GenerateSummary(files),
                CommitMessage = GenerateCommitMessage(input, files),
                Errors = errors,
                Statistics = CalculateStatistics(files)
            };
        }

        private string DetermineFilePath(CodeBlock codeBlock, string targetDirectory)
        {
            // Try to extract file path from code block metadata or comments
            var filePathPattern = @"(?:file:|File:|filename:|Filename:|//|#)\s*([\w/\\.]+\.\w+)";
            var match = Regex.Match(codeBlock.Content, filePathPattern);

            if (match.Success)
            {
                var extractedPath = match.Groups[1].Value;
                return Path.Combine(targetDirectory, extractedPath);
            }

            // Generate file name based on content and language
            var fileName = GenerateFileName(codeBlock);
            return Path.Combine(targetDirectory, fileName);
        }

        private string GenerateFileName(CodeBlock codeBlock)
        {
            var extension = GetFileExtension(codeBlock.Language);
            var baseName = ExtractClassName(codeBlock.Content) ?? "generated";
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            
            return $"{baseName}_{timestamp}{extension}";
        }

        private string GetFileExtension(string language)
        {
            var extensionMap = new Dictionary<string, string>
            {
                ["csharp"] = ".cs",
                ["c#"] = ".cs",
                ["javascript"] = ".js",
                ["typescript"] = ".ts",
                ["python"] = ".py",
                ["java"] = ".java",
                ["swift"] = ".swift",
                ["go"] = ".go",
                ["rust"] = ".rs",
                ["cpp"] = ".cpp",
                ["c++"] = ".cpp",
                ["html"] = ".html",
                ["css"] = ".css",
                ["json"] = ".json",
                ["xml"] = ".xml",
                ["yaml"] = ".yaml",
                ["sql"] = ".sql"
            };

            return extensionMap.GetValueOrDefault(language.ToLower(), ".txt");
        }

        private string ExtractClassName(string code)
        {
            // Extract class name for various languages
            var patterns = new[]
            {
                @"class\s+(\w+)",          // C#, Java, Python, etc.
                @"struct\s+(\w+)",         // C++, Go, Rust
                @"interface\s+(\w+)",      // TypeScript, Java
                @"function\s+(\w+)",       // JavaScript
                @"def\s+(\w+)",            // Python
                @"func\s+(\w+)"            // Go
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(code, pattern);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }

            return null;
        }
    }

    // Code Formatter using Roslyn for C#
    public class CodeFormatter : ICodeFormatter
    {
        public async Task<string> FormatAsync(string code, string language)
        {
            switch (language.ToLower())
            {
                case "csharp":
                case "c#":
                    return await FormatCSharpAsync(code);
                default:
                    // For other languages, return as-is or use language-specific formatters
                    return code;
            }
        }

        private async Task<string> FormatCSharpAsync(string code)
        {
            var tree = CSharpSyntaxTree.ParseText(code);
            var root = await tree.GetRootAsync();
            
            var workspace = new AdhocWorkspace();
            var formattedRoot = Formatter.Format(root, workspace);
            
            return formattedRoot.ToFullString();
        }
    }

    // Syntax Validator
    public class SyntaxValidator : ISyntaxValidator
    {
        public async Task<ValidationResult> ValidateAsync(string code, string language)
        {
            switch (language.ToLower())
            {
                case "csharp":
                case "c#":
                    return await ValidateCSharpAsync(code);
                default:
                    // Basic validation for other languages
                    return new ValidationResult { IsValid = true };
            }
        }

        private async Task<ValidationResult> ValidateCSharpAsync(string code)
        {
            var tree = CSharpSyntaxTree.ParseText(code);
            var diagnostics = tree.GetDiagnostics();
            
            var errors = diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();

            if (errors.Any())
            {
                return new ValidationResult
                {
                    IsValid = false,
                    Error = string.Join("; ", errors.Select(e => e.GetMessage()))
                };
            }

            return new ValidationResult { IsValid = true };
        }
    }
}
```

## 5. Text-to-Speech Service

```csharp
// services/tts-service/Services/TTSService.cs
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Azure.Storage.Blobs;

namespace VoiceCode.TTSService.Services
{
    public interface ITTSService
    {
        Task<AudioResult> GenerateSpeechAsync(TTSRequest request);
    }

    public class TTSService : ITTSService
    {
        private readonly SpeechConfig _speechConfig;
        private readonly BlobServiceClient _blobClient;
        private readonly IDistributedCache _cache;
        private readonly ILogger<TTSService> _logger;

        public TTSService(
            IConfiguration configuration,
            BlobServiceClient blobClient,
            IDistributedCache cache,
            ILogger<TTSService> logger)
        {
            _blobClient = blobClient;
            _cache = cache;
            _logger = logger;
            
            _speechConfig = SpeechConfig.FromSubscription(
                configuration["AzureSpeech:Key"],
                configuration["AzureSpeech:Region"]
            );
        }

        public async Task<AudioResult> GenerateSpeechAsync(TTSRequest request)
        {
            // Check cache
            var cacheKey = GenerateCacheKey(request.Text, request.Voice);
            var cachedUrl = await _cache.GetStringAsync(cacheKey);
            if (!string.IsNullOrEmpty(cachedUrl))
            {
                return new AudioResult
                {
                    Success = true,
                    AudioUrl = cachedUrl,
                    FromCache = true
                };
            }

            // Configure voice
            _speechConfig.SpeechSynthesisVoiceName = request.Voice ?? "en-US-JennyNeural";

            // Generate SSML for better speech quality
            var ssml = BuildSSML(request.Text, request.SpeechStyle);

            using var synthesizer = new SpeechSynthesizer(_speechConfig, null);
            using var result = await synthesizer.SpeakSsmlAsync(ssml);

            if (result.Reason == ResultReason.SynthesizingAudioCompleted)
            {
                // Compress and optimize audio
                var optimizedAudio = await OptimizeAudio(result.AudioData);

                // Upload to blob storage
                var blobUrl = await UploadAudioToBlob(optimizedAudio, request.SessionId);

                // Cache the URL
                await _cache.SetStringAsync(
                    cacheKey,
                    blobUrl,
                    new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
                    }
                );

                return new AudioResult
                {
                    Success = true,
                    AudioUrl = blobUrl,
                    DurationMs = result.AudioDuration.TotalMilliseconds,
                    AudioData = optimizedAudio
                };
            }
            else
            {
                _logger.LogError($"Speech synthesis failed: {result.Reason}");
                throw new InvalidOperationException($"Speech synthesis failed: {result.Reason}");
            }
        }

        private string BuildSSML(string text, SpeechStyle style)
        {
            var rate = style?.Rate ?? "1.0";
            var pitch = style?.Pitch ?? "+0%";
            var emphasis = style?.Emphasis ?? "moderate";

            // Clean and prepare text for SSML
            var cleanedText = SanitizeForSSML(text);

            return $@"
                <speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'>
                    <voice name='en-US-JennyNeural'>
                        <prosody rate='{rate}' pitch='{pitch}'>
                            <emphasis level='{emphasis}'>
                                {cleanedText}
                            </emphasis>
                        </prosody>
                    </voice>
                </speak>
            ";
        }

        private string SanitizeForSSML(string text)
        {
            // Remove or escape special characters
            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        private async Task<byte[]> OptimizeAudio(byte[] audioData)
        {
            // In a real implementation, this would use FFmpeg or similar
            // to compress and optimize the audio for mobile delivery
            // For now, return as-is
            return audioData;
        }

        private async Task<string> UploadAudioToBlob(byte[] audioData, string sessionId)
        {
            var containerClient = _blobClient.GetBlobContainerClient("audio-output");
            await containerClient.CreateIfNotExistsAsync();

            var blobName = $"{sessionId}/{Guid.NewGuid()}.mp3";
            var blobClient = containerClient.GetBlobClient(blobName);

            using var stream = new MemoryStream(audioData);
            await blobClient.UploadAsync(stream, new BlobHttpHeaders
            {
                ContentType = "audio/mpeg"
            });

            return blobClient.Uri.ToString();
        }

        private string GenerateCacheKey(string text, string voice)
        {
            var combined = $"{text}:{voice}";
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
            return Convert.ToBase64String(hash);
        }
    }

    public class AudioResult
    {
        public bool Success { get; set; }
        public string AudioUrl { get; set; }
        public byte[] AudioData { get; set; }
        public double DurationMs { get; set; }
        public bool FromCache { get; set; }
    }
}
```

## 6. Result Dispatcher Service

```csharp
// services/dispatcher/Services/DispatcherService.cs
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.NotificationHubs;
using Microsoft.AspNetCore.SignalR;

namespace VoiceCode.Dispatcher.Services
{
    public interface IDispatcherService
    {
        Task<DispatchResult> DispatchResultAsync(ProcessingResult result);
    }

    public class DispatcherService : IDispatcherService
    {
        private readonly ServiceBusClient _serviceBusClient;
        private readonly INotificationHubClient _notificationHub;
        private readonly IHubContext<ResultHub> _hubContext;
        private readonly ILogger<DispatcherService> _logger;

        public DispatcherService(
            ServiceBusClient serviceBusClient,
            INotificationHubClient notificationHub,
            IHubContext<ResultHub> hubContext,
            ILogger<DispatcherService> logger)
        {
            _serviceBusClient = serviceBusClient;
            _notificationHub = notificationHub;
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task<DispatchResult> DispatchResultAsync(ProcessingResult result)
        {
            var tasks = new List<Task<DispatchChannel>>();

            // 1. Send real-time update via SignalR
            if (!string.IsNullOrEmpty(result.SessionId))
            {
                tasks.Add(SendRealtimeUpdateAsync(result));
            }

            // 2. Queue for Mac Agent if needed
            if (result.RequiresLocalExecution)
            {
                tasks.Add(QueueForMacAgentAsync(result));
            }

            // 3. Send push notification
            if (result.SendNotification)
            {
                tasks.Add(SendPushNotificationAsync(result));
            }

            // Execute all dispatch tasks
            var results = await Task.WhenAll(tasks);

            return new DispatchResult
            {
                Success = results.All(r => r.Success),
                Channels = results.ToList(),
                Timestamp = DateTime.UtcNow
            };
        }

        private async Task<DispatchChannel> SendRealtimeUpdateAsync(ProcessingResult result)
        {
            try
            {
                await _hubContext.Clients.Group(result.SessionId).SendAsync(
                    "ResultUpdate",
                    new
                    {
                        result.Id,
                        result.Status,
                        result.Summary,
                        result.AudioUrl,
                        result.GeneratedCode,
                        Timestamp = DateTime.UtcNow
                    }
                );

                return new DispatchChannel
                {
                    Name = "SignalR",
                    Success = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send SignalR update");
                return new DispatchChannel
                {
                    Name = "SignalR",
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        private async Task<DispatchChannel> QueueForMacAgentAsync(ProcessingResult result)
        {
            try
            {
                var sender = _serviceBusClient.CreateSender("mac-agent-queue");
                
                var message = new ServiceBusMessage
                {
                    Body = BinaryData.FromObjectAsJson(new MacAgentCommand
                    {
                        CommandId = Guid.NewGuid().ToString(),
                        UserId = result.UserId,
                        Action = "ApplyChanges",
                        Files = result.Files,
                        Repository = result.TargetRepository,
                        CommitMessage = result.CommitMessage
                    }),
                    Subject = "FileChanges",
                    TimeToLive = TimeSpan.FromMinutes(10),
                    SessionId = result.UserId // Group by user for ordered delivery
                };

                await sender.SendMessageAsync(message);

                return new DispatchChannel
                {
                    Name = "ServiceBus",
                    Success = true,
                    MessageId = message.MessageId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to queue for Mac agent");
                return new DispatchChannel
                {
                    Name = "ServiceBus",
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        private async Task<DispatchChannel> SendPushNotificationAsync(ProcessingResult result)
        {
            try
            {
                var notification = new Dictionary<string, string>
                {
                    ["title"] = "VoiceCode - Task Completed",
                    ["body"] = result.Summary,
                    ["sessionId"] = result.SessionId,
                    ["resultId"] = result.Id
                };

                var outcome = await _notificationHub.SendTemplateNotificationAsync(
                    notification,
                    $"user:{result.UserId}"
                );

                return new DispatchChannel
                {
                    Name = "PushNotification",
                    Success = true,
                    NotificationId = outcome.NotificationId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send push notification");
                return new DispatchChannel
                {
                    Name = "PushNotification",
                    Success = false,
                    Error = ex.Message
                };
            }
        }
    }

    // SignalR Hub
    public class ResultHub : Hub
    {
        private readonly ILogger<ResultHub> _logger;

        public ResultHub(ILogger<ResultHub> logger)
        {
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            var sessionId = Context.GetHttpContext().Request.Query["sessionId"];
            if (!string.IsNullOrEmpty(sessionId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
                _logger.LogInformation($"Client {Context.ConnectionId} joined session {sessionId}");
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            var sessionId = Context.GetHttpContext().Request.Query["sessionId"];
            if (!string.IsNullOrEmpty(sessionId))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
                _logger.LogInformation($"Client {Context.ConnectionId} left session {sessionId}");
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}
```

## Service Bus Configuration

```csharp
// shared/VoiceCode.Common/ServiceBus/ServiceBusConfiguration.cs
namespace VoiceCode.Common.ServiceBus
{
    public static class ServiceBusConfiguration
    {
        public static IServiceCollection AddServiceBusClient(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddSingleton(provider =>
            {
                var connectionString = configuration.GetConnectionString("ServiceBus");
                return new ServiceBusClient(connectionString);
            });

            return services;
        }

        public static IServiceCollection AddServiceBusProcessor(
            this IServiceCollection services,
            string queueName,
            ServiceBusProcessorOptions options = null)
        {
            services.AddSingleton<ServiceBusProcessor>(provider =>
            {
                var client = provider.GetRequiredService<ServiceBusClient>();
                return client.CreateProcessor(queueName, options ?? new ServiceBusProcessorOptions
                {
                    AutoCompleteMessages = false,
                    MaxConcurrentCalls = 10,
                    PrefetchCount = 20
                });
            });

            return services;
        }
    }
}
```

## Deployment Configuration

```json
// appsettings.Production.json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "your-tenant-id",
    "ClientId": "your-client-id",
    "Audience": "api://voicecode"
  },
  "ConnectionStrings": {
    "ServiceBus": "@Microsoft.KeyVault(SecretUri=https://voicecode-kv.vault.azure.net/secrets/ServiceBusConnection)",
    "CosmosDb": "@Microsoft.KeyVault(SecretUri=https://voicecode-kv.vault.azure.net/secrets/CosmosDbConnection)",
    "Redis": "@Microsoft.KeyVault(SecretUri=https://voicecode-kv.vault.azure.net/secrets/RedisConnection)",
    "Storage": "@Microsoft.KeyVault(SecretUri=https://voicecode-kv.vault.azure.net/secrets/StorageConnection)"
  },
  "AzureSpeech": {
    "Key": "@Microsoft.KeyVault(SecretUri=https://voicecode-kv.vault.azure.net/secrets/SpeechKey)",
    "Region": "eastus"
  },
  "Claude": {
    "ApiKey": "@Microsoft.KeyVault(SecretUri=https://voicecode-kv.vault.azure.net/secrets/ClaudeApiKey)",
    "Model": "claude-3-opus-20240229"
  },
  "ApplicationInsights": {
    "ConnectionString": "@Microsoft.KeyVault(SecretUri=https://voicecode-kv.vault.azure.net/secrets/AppInsightsConnection)"
  }
}
```