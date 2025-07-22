using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Net.Http;

namespace VoiceCode.DispatcherService.Services;

public class ServiceHealthCheck : IHealthCheck
{
    private readonly ILogger<ServiceHealthCheck> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public ServiceHealthCheck(
        ILogger<ServiceHealthCheck> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var services = new[]
        {
            ("STTService", "/health"),
            ("ClaudeService", "/health"),
            ("RouterService", "/health"),
            ("GeneratorService", "/health"),
            ("TTSService", "/health")
        };

        var unhealthyServices = new List<string>();

        foreach (var (serviceName, endpoint) in services)
        {
            try
            {
                var client = _httpClientFactory.CreateClient(serviceName);
                var response = await client.GetAsync(endpoint, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    unhealthyServices.Add(serviceName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check health of {Service}", serviceName);
                unhealthyServices.Add(serviceName);
            }
        }

        if (unhealthyServices.Any())
        {
            return HealthCheckResult.Degraded(
                $"The following services are unhealthy: {string.Join(", ", unhealthyServices)}",
                data: new Dictionary<string, object> { ["unhealthy_services"] = unhealthyServices });
        }

        return HealthCheckResult.Healthy("All services are healthy");
    }
}