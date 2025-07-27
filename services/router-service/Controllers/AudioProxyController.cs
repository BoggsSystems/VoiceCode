using Microsoft.AspNetCore.Mvc;
using System.Net.Http;

namespace VoiceCode.Router.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AudioProxyController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<AudioProxyController> _logger;

        public AudioProxyController(IHttpClientFactory httpClientFactory, ILogger<AudioProxyController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        [HttpGet("fetch")]
        public async Task<IActionResult> FetchAudio([FromQuery] string url)
        {
            try
            {
                if (string.IsNullOrEmpty(url))
                {
                    return BadRequest(new { error = "URL parameter is required" });
                }

                _logger.LogInformation("Proxying audio request for URL: {Url}", url);

                var httpClient = _httpClientFactory.CreateClient();
                
                // Forward any auth headers from the original request
                if (Request.Headers.ContainsKey("Authorization"))
                {
                    httpClient.DefaultRequestHeaders.Add("Authorization", Request.Headers["Authorization"].ToString());
                }

                var response = await httpClient.GetAsync(url);
                
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to fetch audio from {Url}: {StatusCode}", url, response.StatusCode);
                    return StatusCode((int)response.StatusCode, new { error = $"Failed to fetch audio: {response.StatusCode}" });
                }

                var content = await response.Content.ReadAsByteArrayAsync();
                var contentType = response.Content.Headers.ContentType?.ToString() ?? "audio/mpeg";
                
                _logger.LogInformation("Successfully fetched audio, size: {Size} bytes, type: {ContentType}", content.Length, contentType);
                
                // Add CORS headers
                Response.Headers.Add("Access-Control-Allow-Origin", "*");
                Response.Headers.Add("Access-Control-Allow-Methods", "GET, OPTIONS");
                Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
                
                return File(content, contentType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error proxying audio request");
                return StatusCode(500, new { error = "Internal server error while fetching audio" });
            }
        }

        [HttpOptions("fetch")]
        public IActionResult HandleOptions()
        {
            Response.Headers.Add("Access-Control-Allow-Origin", "*");
            Response.Headers.Add("Access-Control-Allow-Methods", "GET, OPTIONS");
            Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
            return Ok();
        }
    }
}