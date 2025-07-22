using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.CognitiveServices.Speech;
using VoiceCode.TTSService.Configuration;

namespace VoiceCode.TTSService.Services;

public class SpeechServiceHealthCheck : IHealthCheck
{
    private readonly ILogger<SpeechServiceHealthCheck> _logger;
    private readonly IOptions<AzureSpeechOptions> _speechOptions;

    public SpeechServiceHealthCheck(
        ILogger<SpeechServiceHealthCheck> logger,
        IOptions<AzureSpeechOptions> speechOptions)
    {
        _logger = logger;
        _speechOptions = speechOptions;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Create a speech config
            SpeechConfig speechConfig;
            if (_speechOptions.Value.UseCustomEndpoint && !string.IsNullOrEmpty(_speechOptions.Value.Endpoint))
            {
                speechConfig = SpeechConfig.FromEndpoint(
                    new Uri(_speechOptions.Value.Endpoint),
                    _speechOptions.Value.Key);
            }
            else
            {
                speechConfig = SpeechConfig.FromSubscription(
                    _speechOptions.Value.Key,
                    _speechOptions.Value.Region);
            }

            // Try to synthesize a simple test phrase
            using var synthesizer = new SpeechSynthesizer(speechConfig, null);
            var result = await synthesizer.SpeakTextAsync("Health check");

            if (result.Reason == ResultReason.SynthesizingAudioCompleted)
            {
                return HealthCheckResult.Healthy("Azure Speech Service is healthy");
            }
            else
            {
                _logger.LogWarning("Speech service health check failed: {Reason}", result.Reason);
                return HealthCheckResult.Unhealthy(
                    $"Azure Speech Service is unhealthy: {result.Reason}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking speech service health");
            return HealthCheckResult.Unhealthy(
                "Azure Speech Service health check failed",
                ex);
        }
    }
}