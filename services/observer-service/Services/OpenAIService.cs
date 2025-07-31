using Azure.AI.OpenAI;
using Azure;
using Microsoft.Extensions.Options;
using VoiceCode.ObserverService.Configuration;

namespace VoiceCode.ObserverService.Services;

public class OpenAIService : IOpenAIService
{
    private readonly ILogger<OpenAIService> _logger;
    private readonly OpenAIClient _client;
    private readonly OpenAIOptions _options;

    public OpenAIService(
        ILogger<OpenAIService> logger,
        IOptions<OpenAIOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        
        _client = new OpenAIClient(
            new Uri(_options.Endpoint),
            new AzureKeyCredential(_options.ApiKey));
    }

    public async Task<string?> GenerateCompletionAsync(string prompt, CancellationToken cancellationToken = default)
    {
        try
        {
            var chatCompletionsOptions = new ChatCompletionsOptions()
            {
                DeploymentName = _options.DeploymentName,
                Messages =
                {
                    new ChatRequestSystemMessage(prompt),
                },
                Temperature = (float)_options.Temperature,
                MaxTokens = _options.MaxTokens,
            };

            var response = await _client.GetChatCompletionsAsync(
                chatCompletionsOptions,
                cancellationToken);

            if (response.Value.Choices.Count > 0)
            {
                return response.Value.Choices[0].Message.Content;
            }

            _logger.LogWarning("No completions returned from OpenAI");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating completion from OpenAI");
            return null;
        }
    }
}