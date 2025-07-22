using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using VoiceCode.STTService.Configuration;
using Microsoft.CognitiveServices.Speech;

namespace VoiceCode.STTService.HealthChecks;

public class SpeechServiceHealthCheck : IHealthCheck
{
    private readonly AzureSpeechOptions _options;
    private readonly ILogger<SpeechServiceHealthCheck> _logger;

    public SpeechServiceHealthCheck(
        IOptions<AzureSpeechOptions> options,
        ILogger<SpeechServiceHealthCheck> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Create speech config
            SpeechConfig speechConfig;
            if (_options.UseCustomEndpoint && !string.IsNullOrEmpty(_options.Endpoint))
            {
                speechConfig = SpeechConfig.FromEndpoint(new Uri(_options.Endpoint), _options.Key);
            }
            else
            {
                speechConfig = SpeechConfig.FromSubscription(_options.Key, _options.Region);
            }

            // Test the connection by creating a recognizer
            // This doesn't actually perform recognition but validates the configuration
            using var audioConfig = AudioConfig.FromDefaultMicrophoneInput();
            using var recognizer = new SpeechRecognizer(speechConfig, audioConfig);

            // Get the endpoint ID to verify connection
            var endpointId = await recognizer.Properties.GetPropertyAsync(
                PropertyId.SpeechServiceConnection_Endpoint,
                cancellationToken);

            if (!string.IsNullOrEmpty(endpointId))
            {
                _logger.LogDebug("Speech service health check passed. Endpoint: {Endpoint}", endpointId);
                
                return HealthCheckResult.Healthy(
                    "Speech service is healthy",
                    new Dictionary<string, object>
                    {
                        { "region", _options.Region },
                        { "endpoint", endpointId }
                    });
            }

            return HealthCheckResult.Unhealthy("Unable to verify speech service connection");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Speech service health check failed");

            return HealthCheckResult.Unhealthy(
                "Speech service is unhealthy",
                ex,
                new Dictionary<string, object>
                {
                    { "error", ex.Message }
                });
        }
    }
}