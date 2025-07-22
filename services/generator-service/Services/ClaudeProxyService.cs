using System.Text;
using System.Text.Json;
using VoiceCode.Common.DTOs;
using VoiceCode.Common.Interfaces;

namespace VoiceCode.GeneratorService.Services;

public class ClaudeProxyService : IClaudeService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ClaudeProxyService> _logger;

    public ClaudeProxyService(HttpClient httpClient, ILogger<ClaudeProxyService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<CodeGenerationResponse> GenerateCodeAsync(CodeGenerationRequest request)
    {
        try
        {
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync("/api/codegeneration/generate", content);
            response.EnsureSuccessStatusCode();
            
            var responseJson = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<CodeGenerationResponse>(responseJson) ?? new CodeGenerationResponse();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call Claude service");
            throw;
        }
    }

    public async Task<string> ExplainCodeAsync(string code, string language)
    {
        try
        {
            var request = new { code, language };
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync("/api/codegeneration/explain", content);
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadFromJsonAsync<ExplainResponse>();
            return result?.Explanation ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to explain code");
            throw;
        }
    }

    public async Task<string> FixCodeAsync(string code, string error, string language)
    {
        try
        {
            var request = new { code, error, language };
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync("/api/codegeneration/fix", content);
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadFromJsonAsync<FixResponse>();
            return result?.FixedCode ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fix code");
            throw;
        }
    }

    public async Task<string> RefactorCodeAsync(string code, string instruction, string language)
    {
        try
        {
            var request = new { code, instructions = instruction, language };
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync("/api/codegeneration/refactor", content);
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadFromJsonAsync<RefactorResponse>();
            return result?.RefactoredCode ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refactor code");
            throw;
        }
    }

    private class ExplainResponse
    {
        public string Explanation { get; set; } = string.Empty;
    }

    private class FixResponse
    {
        public string FixedCode { get; set; } = string.Empty;
    }

    private class RefactorResponse
    {
        public string RefactoredCode { get; set; } = string.Empty;
    }
}