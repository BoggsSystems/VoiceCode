using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace VoiceCode.OrchestratorService.Services
{
    public interface ITTSServiceClient
    {
        Task<string> GenerateAudioAsync(string text, string voice = "en-US-JennyNeural");
    }

    public class TTSServiceClient : ITTSServiceClient
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<TTSServiceClient> _logger;
        private readonly string _ttsServiceUrl;

        public TTSServiceClient(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<TTSServiceClient> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _ttsServiceUrl = configuration["ServiceEndpoints:TTSService"] ?? "https://voicecode-tts.azurewebsites.net";
        }

        public async Task<string> GenerateAudioAsync(string text, string voice = "en-US-JennyNeural")
        {
            try
            {
                _logger.LogInformation("Generating audio for text: {TextLength} characters", text.Length);

                var request = new
                {
                    text = text,
                    voice = voice,
                    language = "en-US",
                    outputFormat = "audio-16khz-128kbitrate-mono-mp3"
                };

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_ttsServiceUrl}/api/tts/synthesize", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<TTSResponse>(responseJson, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    _logger.LogInformation("Audio generated successfully: {AudioUrl}", result?.AudioUrl);
                    return result?.AudioUrl ?? string.Empty;
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("TTS service returned error: {StatusCode} - {Error}", response.StatusCode, error);
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate audio");
                return string.Empty;
            }
        }

        private class TTSResponse
        {
            public string AudioUrl { get; set; }
            public string AudioBase64 { get; set; }
            public double DurationSeconds { get; set; }
        }
    }
}