using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;

namespace VoiceCode.RouterService.Controllers;

[Authorize]
[ApiController]
[Route("api/proxy")]
public class ProxyController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ProxyController> _logger;

    public ProxyController(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ProxyController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("stt/transcribe")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10MB limit
    public async Task<IActionResult> ProxyToSTT()
    {
        var requestId = Guid.NewGuid().ToString();
        _logger.LogInformation("Proxying STT request {RequestId} from user {User}", 
            requestId, User.Identity?.Name);

        try
        {
            // Get STT service URL
            var sttUrl = _configuration["ServiceEndpoints:STT"] ?? 
                         Environment.GetEnvironmentVariable("ServiceEndpoints__STT") ??
                         "https://voicecode-stt.orangewater-a2f689a8.eastus.azurecontainerapps.io";
            
            // Create HTTP client
            var client = _httpClientFactory.CreateClient();
            
            // Forward the request
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, 
                $"{sttUrl}/api/transcription/transcribe");
            
            // Copy form data
            if (Request.HasFormContentType)
            {
                var formData = new MultipartFormDataContent();
                
                foreach (var file in Request.Form.Files)
                {
                    var streamContent = new StreamContent(file.OpenReadStream());
                    streamContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
                    formData.Add(streamContent, file.Name, file.FileName);
                }
                
                foreach (var field in Request.Form)
                {
                    formData.Add(new StringContent(field.Value), field.Key);
                }
                
                requestMessage.Content = formData;
            }
            
            // Forward authorization header to STT service
            // STT service is configured to accept the same custom JWT tokens
            if (Request.Headers.TryGetValue("Authorization", out var authHeader))
            {
                requestMessage.Headers.Authorization = 
                    AuthenticationHeaderValue.Parse(authHeader.ToString());
            }
            
            // Send request to STT service
            var response = await client.SendAsync(requestMessage);
            
            // Return the response
            var content = await response.Content.ReadAsStringAsync();
            
            if (response.IsSuccessStatusCode)
            {
                return Content(content, "application/json");
            }
            else
            {
                _logger.LogWarning("STT service returned error for request {RequestId}: {StatusCode}", 
                    requestId, response.StatusCode);
                return StatusCode((int)response.StatusCode, content);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error proxying STT request {RequestId}", requestId);
            return StatusCode(500, new { error = "Failed to process transcription request", requestId });
        }
    }

    [HttpPost("claude/process")]
    public async Task<IActionResult> ProxyToClaude([FromBody] dynamic request)
    {
        var requestId = Guid.NewGuid().ToString();
        _logger.LogInformation("Proxying Claude request {RequestId} from user {User}", 
            requestId, User.Identity?.Name);

        try
        {
            // Get Claude service URL (could be voice-intelligence or orchestrator)
            var claudeUrl = _configuration["ServiceEndpoints:Claude"] ?? 
                           _configuration["ServiceEndpoints:VoiceIntelligence"] ??
                           Environment.GetEnvironmentVariable("ServiceEndpoints__VoiceIntelligence") ??
                           "https://voicecode-voice-intelligence.orangewater-a2f689a8.eastus.azurecontainerapps.io";
            
            // Create HTTP client
            var client = _httpClientFactory.CreateClient();
            
            // Forward the request
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, 
                $"{claudeUrl}/api/claude/process");
            
            requestMessage.Content = new StringContent(
                request?.ToString() ?? "{}", 
                System.Text.Encoding.UTF8, 
                "application/json");
            
            // Forward authorization header
            if (Request.Headers.TryGetValue("Authorization", out var authHeader))
            {
                requestMessage.Headers.Authorization = 
                    AuthenticationHeaderValue.Parse(authHeader.ToString());
            }
            
            // Send request
            var response = await client.SendAsync(requestMessage);
            
            // Return the response
            var content = await response.Content.ReadAsStringAsync();
            
            if (response.IsSuccessStatusCode)
            {
                return Content(content, "application/json");
            }
            else
            {
                _logger.LogWarning("Claude service returned error for request {RequestId}: {StatusCode}", 
                    requestId, response.StatusCode);
                return StatusCode((int)response.StatusCode, content);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error proxying Claude request {RequestId}", requestId);
            return StatusCode(500, new { error = "Failed to process Claude request", requestId });
        }
    }
}