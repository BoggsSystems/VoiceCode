using System.ClientModel;
using OpenAI;
using OpenAI.Chat;
using VoiceCode.VoiceIntelligenceService.Models;

namespace VoiceCode.VoiceIntelligenceService.Services;

public interface IOpenAISynthesisService
{
    Task<VoiceResponse> SynthesizeResponseAsync(SynthesisRequest request);
}

public class OpenAISynthesisService : IOpenAISynthesisService
{
    private readonly ILogger<OpenAISynthesisService> _logger;
    private readonly ChatClient _chatClient;
    private readonly string _systemPrompt;

    public OpenAISynthesisService(ILogger<OpenAISynthesisService> logger, IConfiguration configuration)
    {
        _logger = logger;
        var apiKey = configuration["OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var client = new OpenAIClient(apiKey);
        _chatClient = client.GetChatClient("gpt-4-turbo-preview");
        
        _systemPrompt = @"You are a voice interaction specialist for a development system with multiple AI workers. 
Your job is to synthesize technical outputs from multiple Claude Code workers into natural, conversational voice responses.

Key responsibilities:
1. Transform verbose, screen-oriented outputs into concise voice responses
2. Aggregate information from multiple workers intelligently
3. Maintain conversational flow and context
4. Keep responses brief but informative (ideal: 2-3 sentences)
5. Use natural speech patterns, not technical jargon
6. When multiple workers have similar findings, summarize rather than list
7. Prioritize actionable information

Guidelines:
- Never read code verbatim; describe what it does
- Convert file listings to counts and highlights
- Transform error messages into helpful explanations
- If workers found different things, highlight the interesting differences
- Always consider what the user likely wants to hear vs see
- End with a natural prompt for follow-up when appropriate

Remember: This will be spoken aloud, so write for the ear, not the eye.";
    }

    public async Task<VoiceResponse> SynthesizeResponseAsync(SynthesisRequest request)
    {
        _logger.LogInformation("Synthesizing response for task {TaskId} with {WorkerCount} worker results", 
            request.TaskId, request.WorkerResults.Count);

        try
        {
            var userPrompt = BuildUserPrompt(request);
            
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(_systemPrompt),
                new UserChatMessage(userPrompt)
            };

            var options = new ChatCompletionOptions
            {
                Temperature = 0.7f,
                MaxOutputTokenCount = 200 // Keep responses concise for voice
            };

            var response = await _chatClient.CompleteChatAsync(messages, options);
            var spokenResponse = response.Value.Content[0].Text;
            
            _logger.LogInformation("Generated voice response: {Response}", spokenResponse);

            return new VoiceResponse
            {
                TaskId = request.TaskId,
                SessionId = request.Context?.SessionId ?? "",
                SpokenResponse = spokenResponse,
                Summary = GenerateSummary(request.WorkerResults),
                WorkersInvolved = request.WorkerResults.Select(r => r.WorkerId).Distinct().ToList(),
                Context = new Dictionary<string, object>
                {
                    ["originalCommand"] = request.OriginalVoiceCommand,
                    ["workerCount"] = request.WorkerResults.Count,
                    ["timestamp"] = DateTime.UtcNow
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to synthesize response for task {TaskId}", request.TaskId);
            
            // Fallback response
            return new VoiceResponse
            {
                TaskId = request.TaskId,
                SpokenResponse = "I've completed the analysis but encountered an issue summarizing the results. " +
                                 $"{request.WorkerResults.Count} workers responded successfully.",
                WorkersInvolved = request.WorkerResults.Select(r => r.WorkerId).Distinct().ToList()
            };
        }
    }

    private string BuildUserPrompt(SynthesisRequest request)
    {
        var prompt = $"Original voice command: \"{request.OriginalVoiceCommand}\"\n\n";
        prompt += "Worker results:\n\n";

        foreach (var result in request.WorkerResults)
        {
            prompt += $"Worker {result.WorkerId}:\n";
            prompt += $"Success: {result.Success}\n";
            if (!string.IsNullOrEmpty(result.Output))
                prompt += $"Output: {result.Output}\n";
            if (!string.IsNullOrEmpty(result.Error))
                prompt += $"Error: {result.Error}\n";
            prompt += "\n";
        }

        if (request.Context?.PreviousResponses?.Any() == true)
        {
            prompt += "\nConversation context:\n";
            prompt += string.Join("\n", request.Context.PreviousResponses.TakeLast(3));
        }

        prompt += "\nSynthesize a natural voice response for the user based on these results.";
        return prompt;
    }

    private string GenerateSummary(List<WorkerResult> results)
    {
        var successful = results.Count(r => r.Success);
        var failed = results.Count(r => !r.Success);
        return $"{successful} successful, {failed} failed out of {results.Count} workers";
    }
}