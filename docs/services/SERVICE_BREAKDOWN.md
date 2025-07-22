# Service Breakdown

## 1. Speech-to-Text Service

### Overview
Converts voice audio from iOS app into text transcripts for processing.

### Technology Stack
- **Primary**: Azure Speech Services
- **Alternative**: OpenAI Whisper API
- **Runtime**: ASP.NET Core Web API (C#/.NET 8)
- **Hosting**: Azure Web App
- **Communication**: REST API + Service Bus

### Responsibilities
- Accept audio streams (WAV, M4A, MP3)
- Normalize audio format and quality
- Perform speech recognition
- Return transcribed text with confidence scores
- Support multiple languages
- Handle ambient noise and accents

### Implementation

```csharp
// services/stt-service/Controllers/TranscriptionController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using System.Text.Json;

namespace VoiceCode.STTService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TranscriptionController : ControllerBase
    {
        private readonly SpeechConfig _speechConfig;
        private readonly ILogger<TranscriptionController> _logger;

        public TranscriptionController(IConfiguration configuration, ILogger<TranscriptionController> logger)
        {
            _logger = logger;
            _speechConfig = SpeechConfig.FromSubscription(
                configuration["AzureSpeech:Key"],
                configuration["AzureSpeech:Region"]
            );
        }

        [HttpPost("transcribe")]
        public async Task<IActionResult> TranscribeAudio(
            [FromBody] byte[] audioData,
            [FromQuery] string language = "en-US")
        {
            try
            {
                _speechConfig.SpeechRecognitionLanguage = language;

                using var audioStream = new MemoryStream(audioData);
                using var audioConfig = AudioConfig.FromStreamInput(audioStream);
                using var recognizer = new SpeechRecognizer(_speechConfig, audioConfig);

                var result = await recognizer.RecognizeOnceAsync();

                if (result.Reason == ResultReason.RecognizedSpeech)
                {
                    var response = new TranscriptionResult
                    {
                        Transcript = result.Text,
                        Confidence = result.Properties.GetProperty(PropertyId.SpeechServiceResponse_JsonResult),
                        Language = language,
                        DurationMs = result.Duration.TotalMilliseconds
                    };

                    return Ok(response);
                }
                else
                {
                    _logger.LogError($"Recognition failed: {result.Reason}");
                    return StatusCode(500, new { error = $"Recognition failed: {result.Reason}" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Transcription failed");
                return StatusCode(500, new { error = "Transcription failed", details = ex.Message });
            }
        }
    }

    public class TranscriptionResult
    {
        public string Transcript { get; set; }
        public string Confidence { get; set; }
        public string Language { get; set; }
        public double DurationMs { get; set; }
    }
}

// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddApplicationInsightsTelemetry();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
```

### API Specification

```yaml
openapi: 3.0.0
paths:
  /api/stt/transcribe:
    post:
      summary: Transcribe audio to text
      requestBody:
        content:
          audio/*:
            schema:
              type: string
              format: binary
      parameters:
        - name: language
          in: query
          schema:
            type: string
            default: en-US
      responses:
        200:
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/TranscriptionResult'
```

---

## 2. Claude Interaction Service

### Overview
Manages all interactions with Anthropic's Claude API for code generation and analysis.

### Technology Stack
- **Runtime**: ASP.NET Core Web API (C#/.NET 8)
- **Hosting**: Azure Web App
- **API Client**: HTTP Client with Anthropic API
- **Caching**: Azure Redis Cache
- **Monitoring**: Application Insights

### Responsibilities
- Construct optimized prompts for Claude
- Manage API rate limits and quotas
- Handle token budgeting
- Parse and validate Claude responses
- Implement retry logic with exponential backoff
- Cache frequent requests

### Implementation

```csharp
// services/claude-service/Controllers/ClaudeController.cs
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Net.Http.Headers;
using StackExchange.Redis;

namespace VoiceCode.ClaudeService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ClaudeController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConnectionMultiplexer _redis;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ClaudeController> _logger;

        public ClaudeController(
            IHttpClientFactory httpClientFactory,
            IConnectionMultiplexer redis,
            IConfiguration configuration,
            ILogger<ClaudeController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _redis = redis;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpPost("generate")]
        public async Task<IActionResult> GenerateCode([FromBody] CodeGenerationRequest request)
        {
            try
            {
                // Check cache
                var db = _redis.GetDatabase();
                var cacheKey = GenerateCacheKey(request);
                var cached = await db.StringGetAsync(cacheKey);
                if (!cached.IsNullOrEmpty)
                {
                    return Ok(JsonSerializer.Deserialize<CodeGenerationResponse>(cached));
                }

                // Build prompt
                var prompt = BuildPrompt(request);

                // Check token budget
                var estimatedTokens = EstimateTokens(prompt);
                if (estimatedTokens > request.MaxTokens)
                {
                    return BadRequest(new { error = "Prompt exceeds token budget" });
                }

                // Call Claude API
                var response = await CallClaudeAPI(prompt, request.MaxTokens);

                // Cache result
                await db.StringSetAsync(
                    cacheKey,
                    JsonSerializer.Serialize(response),
                    TimeSpan.FromHours(1)
                );

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Claude service error");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private async Task<CodeGenerationResponse> CallClaudeAPI(string prompt, int maxTokens)
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("x-api-key", _configuration["Claude:ApiKey"]);
            client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var requestBody = new
            {
                model = "claude-3-opus-20240229",
                max_tokens = maxTokens,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                temperature = 0.2
            };

            var response = await client.PostAsJsonAsync(
                "https://api.anthropic.com/v1/messages",
                requestBody
            );

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var claudeResponse = JsonSerializer.Deserialize<ClaudeAPIResponse>(content);

            return ParseClaudeResponse(claudeResponse);
        }

        private CodeGenerationResponse ParseClaudeResponse(ClaudeAPIResponse apiResponse)
        {
            var content = apiResponse.Content[0].Text;
            
            return new CodeGenerationResponse
            {
                Code = ExtractCodeBlocks(content),
                Explanation = ExtractExplanation(content),
                Confidence = CalculateConfidence(apiResponse),
                SuggestedFiles = ExtractFileNames(content),
                Dependencies = ExtractDependencies(content)
            };
        }

        private string BuildPrompt(CodeGenerationRequest request)
        {
            return $@"
                Instruction: {request.Instruction}
                Context: {request.Context}
                Language: {request.Language}
                Coding Style: {request.CodingStyle}

                Please generate the code following best practices and the specified style guide.
            ";
        }

        private int EstimateTokens(string text)
        {
            // Simple estimation: ~4 characters per token
            return text.Length / 4;
        }

        private string GenerateCacheKey(CodeGenerationRequest request)
        {
            var combined = $"{request.Instruction}:{request.Language}:{request.CodingStyle}";
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(combined));
            return Convert.ToBase64String(hash);
        }

        private List<CodeBlock> ExtractCodeBlocks(string content)
        {
            var codeBlocks = new List<CodeBlock>();
            var pattern = @"```(\w+)?\n(.*?)```";
            var matches = System.Text.RegularExpressions.Regex.Matches(
                content,
                pattern,
                System.Text.RegularExpressions.RegexOptions.Singleline
            );

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                codeBlocks.Add(new CodeBlock
                {
                    Language = match.Groups[1].Value,
                    Content = match.Groups[2].Value.Trim()
                });
            }

            return codeBlocks;
        }

        private string ExtractExplanation(string content)
        {
            // Remove code blocks and return remaining text
            var withoutCode = System.Text.RegularExpressions.Regex.Replace(
                content,
                @"```.*?```",
                "",
                System.Text.RegularExpressions.RegexOptions.Singleline
            );
            return withoutCode.Trim();
        }

        private List<string> ExtractFileNames(string content)
        {
            var fileNames = new List<string>();
            var pattern = @"(?:file:|File:|filename:|Filename:)\s*([^\s,]+)";
            var matches = System.Text.RegularExpressions.Regex.Matches(content, pattern);

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                fileNames.Add(match.Groups[1].Value);
            }

            return fileNames;
        }

        private List<string> ExtractDependencies(string content)
        {
            var dependencies = new List<string>();
            // Extract package references, imports, etc.
            var patterns = new[]
            {
                @"using\s+([^\s;]+)",  // C# usings
                @"import\s+.*?from\s+['""]([^'""]+)['""]",  // JS/TS imports
                @"require\(['""]([^'""]+)['""]\)",  // Node require
                @"<PackageReference\s+Include=""([^""]+)"""  // .NET packages
            };

            foreach (var pattern in patterns)
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(content, pattern);
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    dependencies.Add(match.Groups[1].Value);
                }
            }

            return dependencies.Distinct().ToList();
        }

        private double CalculateConfidence(ClaudeAPIResponse response)
        {
            // Simple confidence calculation based on response
            return 0.95; // You can implement more sophisticated logic here
        }
    }

    // Models
    public class CodeGenerationRequest
    {
        public string Instruction { get; set; }
        public string Context { get; set; }
        public string Language { get; set; }
        public string CodingStyle { get; set; }
        public int MaxTokens { get; set; } = 4000;
    }

    public class CodeGenerationResponse
    {
        public List<CodeBlock> Code { get; set; }
        public string Explanation { get; set; }
        public double Confidence { get; set; }
        public List<string> SuggestedFiles { get; set; }
        public List<string> Dependencies { get; set; }
    }

    public class CodeBlock
    {
        public string Language { get; set; }
        public string Content { get; set; }
    }

    public class ClaudeAPIResponse
    {
        public List<ClaudeContent> Content { get; set; }
    }

    public class ClaudeContent
    {
        public string Type { get; set; }
        public string Text { get; set; }
    }
}

// Program.cs additions
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis"))
);
```

---

## 3. Prompt Router and Context Builder

### Overview
Intelligently routes requests and builds context-aware prompts for optimal Claude responses.

### Technology Stack
- **Runtime**: ASP.NET Core Web API (C#/.NET 8)
- **Hosting**: Azure Web App
- **Storage**: Cosmos DB for context
- **ML**: Azure Cognitive Services for intent classification
- **Queue**: Azure Service Bus for routing

### Responsibilities
- Classify user intent from transcripts
- Retrieve relevant context from history
- Select appropriate prompt templates
- Manage conversation context
- Route to specialized handlers
- Track token usage across requests

### Implementation

```typescript
// services/prompt-router/src/router.ts
import { IntentClassifier } from './intent-classifier';
import { ContextManager } from './context-manager';
import { PromptBuilder } from './prompt-builder';
import { ServiceBusClient } from '@azure/service-bus';

export class PromptRouter {
    private classifier: IntentClassifier;
    private contextManager: ContextManager;
    private promptBuilder: PromptBuilder;
    private serviceBus: ServiceBusClient;
    
    async routeRequest(transcript: string, userId: string, sessionId: string): Promise<RoutingResult> {
        // Classify intent
        const intent = await this.classifier.classify(transcript);
        
        // Retrieve context
        const context = await this.contextManager.getContext({
            userId,
            sessionId,
            lookbackWindow: 10 // Last 10 interactions
        });
        
        // Build enhanced prompt
        const enhancedPrompt = this.promptBuilder.build({
            transcript,
            intent,
            context,
            userPreferences: context.preferences
        });
        
        // Determine routing
        const route = this.determineRoute(intent);
        
        // Send to appropriate queue
        await this.serviceBus.sendMessage({
            body: {
                prompt: enhancedPrompt,
                metadata: {
                    userId,
                    sessionId,
                    intent,
                    timestamp: new Date().toISOString()
                }
            },
            subject: route
        });
        
        return {
            route,
            intent,
            contextUsed: context.items.length
        };
    }
    
    private determineRoute(intent: Intent): string {
        const routeMap = {
            'code_generation': 'claude-service',
            'code_explanation': 'claude-service',
            'bug_fix': 'claude-service',
            'refactor': 'claude-service',
            'test_generation': 'claude-service',
            'documentation': 'claude-service',
            'file_operation': 'file-service',
            'search': 'search-service'
        };
        
        return routeMap[intent.type] || 'claude-service';
    }
}
```

---

## 4. Code File Generator

### Overview
Transforms Claude's code output into properly formatted files and change sets.

### Technology Stack
- **Runtime**: Azure Functions (TypeScript)
- **Parser**: Tree-sitter for code parsing
- **Formatter**: Prettier API
- **Diff**: diff-match-patch library

### Responsibilities
- Parse Claude's code output
- Generate proper file structures
- Apply code formatting standards
- Create file diffs for existing code
- Validate syntax before saving
- Generate commit messages

### Implementation

```typescript
// services/code-generator/src/generator.ts
import { Parser } from 'tree-sitter';
import * as prettier from 'prettier';
import { DiffMatchPatch } from 'diff-match-patch';

export class CodeGenerator {
    private parser: Parser;
    private differ: DiffMatchPatch;
    
    async generateFiles(claudeOutput: ClaudeOutput): Promise<GeneratedFiles> {
        const files: FileChange[] = [];
        
        for (const codeBlock of claudeOutput.codeBlocks) {
            // Determine file path
            const filePath = this.determineFilePath(codeBlock);
            
            // Format code
            const formattedCode = await this.formatCode(
                codeBlock.content,
                codeBlock.language
            );
            
            // Validate syntax
            const validation = await this.validateSyntax(
                formattedCode,
                codeBlock.language
            );
            
            if (!validation.valid) {
                throw new Error(`Syntax error in ${filePath}: ${validation.error}`);
            }
            
            // Check if file exists
            const existingContent = await this.getExistingFile(filePath);
            
            if (existingContent) {
                // Generate diff
                const diff = this.differ.diff_main(existingContent, formattedCode);
                files.push({
                    path: filePath,
                    action: 'modify',
                    content: formattedCode,
                    diff: diff,
                    originalContent: existingContent
                });
            } else {
                files.push({
                    path: filePath,
                    action: 'create',
                    content: formattedCode
                });
            }
        }
        
        return {
            files,
            summary: this.generateSummary(files),
            commitMessage: this.generateCommitMessage(claudeOutput, files)
        };
    }
    
    private async formatCode(code: string, language: string): Promise<string> {
        const config = await prettier.resolveConfig(process.cwd());
        
        return prettier.format(code, {
            ...config,
            parser: this.getParser(language)
        });
    }
    
    private generateCommitMessage(output: ClaudeOutput, files: FileChange[]): string {
        const action = files.some(f => f.action === 'create') ? 'Add' : 'Update';
        const fileCount = files.length;
        const summary = output.explanation.split('.')[0];
        
        return `${action} ${fileCount} file${fileCount > 1 ? 's' : ''}: ${summary}`;
    }
}
```

---

## 5. Text-to-Speech Feedback Generator

### Overview
Converts Claude's responses into natural speech for audio feedback.

### Technology Stack
- **Runtime**: Azure Functions (Python)
- **TTS Engine**: Azure Neural TTS
- **Audio Processing**: ffmpeg
- **Caching**: Blob Storage

### Responsibilities
- Summarize code changes for speech
- Generate natural language explanations
- Convert text to speech audio
- Optimize audio for mobile playback
- Cache frequently used phrases
- Support multiple voices/languages

### Implementation

```python
# services/tts-service/main.py
import azure.cognitiveservices.speech as speechsdk
import asyncio
from azure.storage.blob import BlobServiceClient
import hashlib
import ffmpeg

class TTSService:
    def __init__(self):
        self.speech_config = speechsdk.SpeechConfig(
            subscription=os.environ["AZURE_SPEECH_KEY"],
            region=os.environ["AZURE_SPEECH_REGION"]
        )
        self.blob_client = BlobServiceClient.from_connection_string(
            os.environ["AZURE_STORAGE_CONNECTION"]
        )
        
    async def generate_speech(self, text: str, voice: str = "en-US-JennyNeural") -> bytes:
        # Check cache
        cache_key = self._generate_cache_key(text, voice)
        cached_audio = await self._get_cached_audio(cache_key)
        if cached_audio:
            return cached_audio
            
        # Configure voice
        self.speech_config.speech_synthesis_voice_name = voice
        
        # Generate speech
        synthesizer = speechsdk.SpeechSynthesizer(
            speech_config=self.speech_config,
            audio_config=None  # Return audio data
        )
        
        # Add SSML for better prosody
        ssml = self._build_ssml(text)
        result = synthesizer.speak_ssml_async(ssml).get()
        
        if result.reason == speechsdk.ResultReason.SynthesizingAudioCompleted:
            # Compress audio
            compressed = await self._compress_audio(result.audio_data)
            
            # Cache result
            await self._cache_audio(cache_key, compressed)
            
            return compressed
        else:
            raise Exception(f"Speech synthesis failed: {result.reason}")
            
    def _build_ssml(self, text: str) -> str:
        # Enhance speech with SSML markup
        return f"""
        <speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="en-US">
            <voice name="en-US-JennyNeural">
                <prosody rate="1.1" pitch="+5%">
                    {text}
                </prosody>
            </voice>
        </speak>
        """
        
    async def _compress_audio(self, audio_data: bytes) -> bytes:
        # Use ffmpeg to compress audio for mobile
        process = (
            ffmpeg
            .input('pipe:', format='wav')
            .output('pipe:', format='aac', audio_bitrate='64k')
            .run_async(pipe_stdin=True, pipe_stdout=True)
        )
        
        output, _ = process.communicate(input=audio_data)
        return output
```

---

## 6. Result Dispatcher

### Overview
Orchestrates delivery of results to iOS app and Mac agent through multiple channels.

### Technology Stack
- **Runtime**: Azure Functions (TypeScript)
- **Real-time**: SignalR Service
- **Push**: Notification Hubs
- **Queue**: Service Bus
- **State**: Redis Cache

### Responsibilities
- Route results to appropriate destinations
- Manage delivery state and retries
- Send real-time updates via SignalR
- Queue messages for Mac agent
- Send push notifications
- Track delivery confirmation

### Implementation

```typescript
// services/dispatcher/src/dispatcher.ts
import { ServiceBusClient } from '@azure/service-bus';
import { HubServiceClient } from '@azure/notification-hubs';
import * as signalR from '@microsoft/signalr';
import { RedisClient } from 'redis';

export class ResultDispatcher {
    private serviceBus: ServiceBusClient;
    private notificationHub: HubServiceClient;
    private signalRConnection: signalR.HubConnection;
    private redis: RedisClient;
    
    async dispatch(result: ProcessingResult): Promise<DispatchResult> {
        const dispatches: Promise<any>[] = [];
        
        // 1. Send real-time update to iOS app
        if (result.sessionId) {
            dispatches.push(
                this.sendRealtimeUpdate(result.sessionId, {
                    status: 'completed',
                    summary: result.summary,
                    audioUrl: result.audioUrl,
                    code: result.generatedCode
                })
            );
        }
        
        // 2. Queue for Mac agent if needed
        if (result.requiresLocalExecution) {
            dispatches.push(
                this.queueForMacAgent({
                    userId: result.userId,
                    action: 'apply_changes',
                    files: result.files,
                    repository: result.targetRepository
                })
            );
        }
        
        // 3. Send push notification
        if (result.sendNotification) {
            dispatches.push(
                this.sendPushNotification(
                    result.userId,
                    {
                        title: 'Code Generated',
                        body: result.summary,
                        data: {
                            sessionId: result.sessionId,
                            resultId: result.id
                        }
                    }
                )
            );
        }
        
        // 4. Update state
        await this.updateDeliveryState(result.id, 'dispatching');
        
        // Execute all dispatches
        try {
            await Promise.all(dispatches);
            await this.updateDeliveryState(result.id, 'delivered');
            
            return {
                success: true,
                deliveredTo: ['ios_app', 'mac_agent', 'push'],
                timestamp: new Date().toISOString()
            };
        } catch (error) {
            await this.updateDeliveryState(result.id, 'failed');
            throw error;
        }
    }
    
    private async sendRealtimeUpdate(sessionId: string, update: any): Promise<void> {
        await this.signalRConnection.invoke(
            'SendToSession',
            sessionId,
            'ResultUpdate',
            update
        );
    }
    
    private async queueForMacAgent(message: MacAgentMessage): Promise<void> {
        const sender = this.serviceBus.createSender('mac-agent-queue');
        
        await sender.sendMessages({
            body: message,
            contentType: 'application/json',
            timeToLive: 300, // 5 minutes
            userProperties: {
                userId: message.userId,
                priority: 'normal'
            }
        });
    }
    
    private async sendPushNotification(userId: string, notification: PushNotification): Promise<void> {
        const message = {
            body: notification.body,
            title: notification.title,
            data: notification.data,
            badge: 1
        };
        
        await this.notificationHub.sendNotification(
            message,
            {
                tags: [`user:${userId}`]
            }
        );
    }
}
```