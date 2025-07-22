using System.Text;
using System.Text.Json;
using VoiceCode.Common.Models;
using VoiceCode.DispatcherService.Hubs;

namespace VoiceCode.DispatcherService.Services;

public interface IServiceRouter
{
    Task<TranscriptionResult> SendToSTTAsync(AudioMessage audio, string sessionId);
    Task<RoutingResult> SendToRouterAsync(string text, UserSession session);
    Task<ClaudeResponse> SendToClaudeAsync(ClaudeRequest request, string sessionId);
    Task<GeneratedFiles> SendToGeneratorAsync(CodeGenerationInput input, string sessionId);
    Task<SynthesisResult> SendToTTSAsync(SynthesisRequest request, string sessionId);
}

public class ServiceRouter : IServiceRouter
{
    private readonly ILogger<ServiceRouter> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public ServiceRouter(
        ILogger<ServiceRouter> logger,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<TranscriptionResult> SendToSTTAsync(AudioMessage audio, string sessionId)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("STTService");
            
            var request = new
            {
                audioData = audio.AudioData,
                format = audio.Format,
                sampleRate = audio.SampleRate,
                language = audio.Language ?? "en-US",
                sessionId = sessionId
            };

            var response = await client.PostAsJsonAsync("/api/speech/transcribe", request);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<TranscriptionResult>();
            return result ?? new TranscriptionResult { Success = false, Error = "No response from STT service" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending to STT service");
            return new TranscriptionResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    public async Task<RoutingResult> SendToRouterAsync(string text, UserSession session)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("RouterService");
            
            var request = new
            {
                text = text,
                context = session.Context,
                sessionId = session.Id
            };

            var response = await client.PostAsJsonAsync("/api/prompt/route", request);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<RoutingResult>();
            return result ?? new RoutingResult 
            { 
                Intent = new Intent { Category = IntentCategory.Unknown },
                Priority = MessagePriority.Low
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending to Router service");
            return new RoutingResult
            {
                Intent = new Intent { Category = IntentCategory.Unknown },
                Priority = MessagePriority.Low,
                Error = ex.Message
            };
        }
    }

    public async Task<ClaudeResponse> SendToClaudeAsync(ClaudeRequest request, string sessionId)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ClaudeService");
            
            request.SessionId = sessionId;

            var response = await client.PostAsJsonAsync("/api/claude/process", request);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ClaudeResponse>();
            return result ?? new ClaudeResponse { Success = false, Error = "No response from Claude service" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending to Claude service");
            return new ClaudeResponse
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    public async Task<GeneratedFiles> SendToGeneratorAsync(CodeGenerationInput input, string sessionId)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("GeneratorService");
            
            var request = new
            {
                codeBlocks = input.CodeBlocks,
                targetDirectory = input.TargetDirectory,
                sessionId = sessionId
            };

            var response = await client.PostAsJsonAsync("/api/generator/generate", request);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<GeneratedFiles>();
            return result ?? new GeneratedFiles();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending to Generator service");
            return new GeneratedFiles
            {
                Errors = new List<string> { ex.Message }
            };
        }
    }

    public async Task<SynthesisResult> SendToTTSAsync(SynthesisRequest request, string sessionId)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("TTSService");
            
            request.SessionId = sessionId;

            var response = await client.PostAsJsonAsync("/api/speech/synthesize", request);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<SynthesisResult>();
            return result ?? new SynthesisResult { AudioUrl = string.Empty };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending to TTS service");
            return new SynthesisResult
            {
                AudioUrl = string.Empty,
                Error = ex.Message
            };
        }
    }
}