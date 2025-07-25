using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VoiceCode.TTSService.Configuration;
using VoiceCode.Common.Models;

namespace VoiceCode.TTSService.Services;

/// <summary>
/// Alternative TTS implementation using Azure Cognitive Services REST API
/// </summary>
public class AzureTTSRestService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AzureTTSRestService> _logger;
    private readonly IOptions<AzureSpeechOptions> _speechOptions;
    private readonly string _tokenEndpoint;
    private readonly string _ttsEndpoint;
    private string? _cachedToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    public AzureTTSRestService(
        HttpClient httpClient,
        ILogger<AzureTTSRestService> logger,
        IOptions<AzureSpeechOptions> speechOptions)
    {
        _httpClient = httpClient;
        _logger = logger;
        _speechOptions = speechOptions;
        
        var region = _speechOptions.Value.Region;
        _tokenEndpoint = $"https://{region}.api.cognitive.microsoft.com/sts/v1.0/issueToken";
        _ttsEndpoint = $"https://{region}.tts.speech.microsoft.com/cognitiveservices/v1";
    }

    public async Task<byte[]> SynthesizeSpeechAsync(string ssml, VoiceProfile profile)
    {
        try
        {
            // Get or refresh token
            var token = await GetTokenAsync();
            
            // Prepare request
            using var request = new HttpRequestMessage(HttpMethod.Post, _ttsEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-Microsoft-OutputFormat", GetOutputFormat());
            request.Headers.Add("User-Agent", "VoiceCode-TTS-Service");
            
            // Set content
            request.Content = new StringContent(ssml, Encoding.UTF8, "application/ssml+xml");
            
            // Send request
            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                var audioData = await response.Content.ReadAsByteArrayAsync();
                _logger.LogInformation("Successfully synthesized {Bytes} bytes of audio", audioData.Length);
                return audioData;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("TTS API error: {StatusCode} - {Error}. SSML was: {SSML}", response.StatusCode, error, ssml);
                throw new ApplicationException($"TTS API error: {response.StatusCode} - {error}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error synthesizing speech via REST API");
            throw;
        }
    }

    private async Task<string> GetTokenAsync()
    {
        if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry)
        {
            return _cachedToken;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _tokenEndpoint);
        request.Headers.Add("Ocp-Apim-Subscription-Key", _speechOptions.Value.Key);
        request.Content = new StringContent(string.Empty);

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        _cachedToken = await response.Content.ReadAsStringAsync();
        _tokenExpiry = DateTime.UtcNow.AddMinutes(9); // Token is valid for 10 minutes, refresh at 9
        
        _logger.LogInformation("Refreshed TTS authentication token");
        return _cachedToken;
    }

    private string GetOutputFormat()
    {
        // Using default MP3 format for now
        // This can be extended to support different formats based on configuration
        return "audio-16khz-32kbitrate-mono-mp3";
    }
}