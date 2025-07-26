using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using VoiceCode.WorkerService.Models;

namespace VoiceCode.WorkerService.Services;

public interface IClaudeApiService
{
    Task<ClaudeFileOperationResponse> GenerateFileOperationsAsync(
        string voiceCommand, 
        RepositoryContext context, 
        CancellationToken cancellationToken = default);
}

public class ClaudeApiService : IClaudeApiService
{
    private readonly ILogger<ClaudeApiService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly JsonSerializerOptions _jsonOptions;
    
    public ClaudeApiService(
        ILogger<ClaudeApiService> logger,
        HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
        _apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? 
                  throw new InvalidOperationException("ANTHROPIC_API_KEY environment variable is not set");
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
    }
    
    public async Task<ClaudeFileOperationResponse> GenerateFileOperationsAsync(
        string voiceCommand, 
        RepositoryContext context, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating file operations for command: {Command}", voiceCommand);
        
        try
        {
            var systemPrompt = BuildSystemPrompt();
            var userPrompt = BuildUserPrompt(voiceCommand, context);
            
            var request = new ClaudeApiRequest
            {
                Model = "claude-3-5-sonnet-20241022",
                MaxTokens = 4096,
                Temperature = 0.2,
                System = systemPrompt,
                Messages = new List<ClaudeMessage>
                {
                    new() { Role = "user", Content = userPrompt }
                }
            };
            
            var response = await CallClaudeApiAsync(request, cancellationToken);
            
            // Parse the structured response
            var fileOperations = ParseFileOperations(response);
            
            _logger.LogInformation("Generated {Count} file operations", fileOperations.Operations.Count);
            
            return fileOperations;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating file operations");
            throw;
        }
    }
    
    private string BuildSystemPrompt()
    {
        return @"You are an expert software developer assistant integrated into a voice-controlled development system. Your task is to interpret voice commands and generate precise file operations to implement the requested functionality.

You must respond with valid JSON following this exact structure:
{
  ""operations"": [
    {
      ""type"": ""create|edit|delete|read"",
      ""path"": ""relative/path/to/file"",
      ""content"": ""full file content for create operations"",
      ""oldContent"": ""exact content to find for edit operations"",
      ""newContent"": ""replacement content for edit operations"",
      ""reason"": ""brief explanation of this operation""
    }
  ],
  ""summary"": ""A concise summary of what was implemented"",
  ""explanation"": ""Optional detailed explanation of the implementation approach""
}

Guidelines:
1. For CREATE operations: provide complete, working file content
2. For EDIT operations: provide exact oldContent to match and newContent to replace
3. Use relative paths from the repository root
4. Follow the project's existing code style and conventions
5. Ensure all code is syntactically correct and follows best practices
6. Create all necessary files for a complete, working implementation
7. Consider the project type and use appropriate frameworks/libraries already in the project
8. For file edits, match the exact indentation and formatting

Remember: You're implementing real code that will be executed. Make it production-ready.";
    }
    
    private string BuildUserPrompt(string voiceCommand, RepositoryContext context)
    {
        var prompt = new StringBuilder();
        
        prompt.AppendLine($"Voice Command: {voiceCommand}");
        prompt.AppendLine();
        prompt.AppendLine("Repository Context:");
        prompt.AppendLine($"- Project Type: {context.ProjectType}");
        prompt.AppendLine($"- Root Path: {context.RootPath}");
        
        if (!string.IsNullOrEmpty(context.CurrentBranch))
        {
            prompt.AppendLine($"- Current Branch: {context.CurrentBranch}");
        }
        
        if (context.RelevantFiles.Any())
        {
            prompt.AppendLine();
            prompt.AppendLine("Relevant Files in Repository:");
            foreach (var file in context.RelevantFiles.Take(20))
            {
                prompt.AppendLine($"- {file}");
            }
        }
        
        if (context.FileContents.Any())
        {
            prompt.AppendLine();
            prompt.AppendLine("Key File Contents:");
            foreach (var (path, content) in context.FileContents)
            {
                prompt.AppendLine($"\n--- {path} ---");
                prompt.AppendLine(content.Length > 1000 ? content.Substring(0, 1000) + "..." : content);
            }
        }
        
        prompt.AppendLine();
        prompt.AppendLine("Generate the file operations needed to implement the voice command. Return ONLY valid JSON.");
        
        return prompt.ToString();
    }
    
    private async Task<ClaudeApiResponse> CallClaudeApiAsync(ClaudeApiRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Calling Claude API with model: {Model}", request.Model);
        
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        httpRequest.Headers.Add("x-api-key", _apiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(request, _jsonOptions),
            Encoding.UTF8,
            "application/json");
        
        var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken);
        
        if (!httpResponse.IsSuccessStatusCode)
        {
            var error = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Claude API error: {StatusCode} - {Error}", httpResponse.StatusCode, error);
            throw new HttpRequestException($"Claude API error: {httpResponse.StatusCode} - {error}");
        }
        
        var responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
        var response = JsonSerializer.Deserialize<ClaudeApiResponse>(responseJson, _jsonOptions);
        
        if (response == null || !response.Content.Any())
        {
            throw new InvalidOperationException("Empty response from Claude API");
        }
        
        _logger.LogInformation("Claude API response received. Tokens used - Input: {Input}, Output: {Output}", 
            response.Usage?.InputTokens, response.Usage?.OutputTokens);
        
        return response;
    }
    
    private ClaudeFileOperationResponse ParseFileOperations(ClaudeApiResponse response)
    {
        var content = response.Content.FirstOrDefault()?.Text;
        
        if (string.IsNullOrEmpty(content))
        {
            throw new InvalidOperationException("No text content in Claude response");
        }
        
        _logger.LogDebug("Parsing Claude response: {Content}", content.Length > 500 ? content.Substring(0, 500) + "..." : content);
        
        try
        {
            // Extract JSON from the response (Claude might include explanation text)
            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}') + 1;
            
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = content.Substring(jsonStart, jsonEnd - jsonStart);
                var operations = JsonSerializer.Deserialize<ClaudeFileOperationResponse>(json, _jsonOptions);
                
                if (operations == null)
                {
                    throw new InvalidOperationException("Failed to deserialize file operations");
                }
                
                return operations;
            }
            
            throw new InvalidOperationException("No valid JSON found in Claude response");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Claude response as JSON");
            
            // Return a fallback response
            return new ClaudeFileOperationResponse
            {
                Summary = "Failed to parse Claude's response",
                Explanation = content,
                Operations = new List<ClaudeFileOperation>()
            };
        }
    }
}