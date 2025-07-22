using Microsoft.Extensions.Options;
using VoiceCode.DispatcherService.Configuration;

namespace VoiceCode.DispatcherService.Services;

public class SessionCleanupService : BackgroundService
{
    private readonly ILogger<SessionCleanupService> _logger;
    private readonly IOptions<DispatcherOptions> _options;
    private readonly SessionManager _sessionManager;
    private readonly IMetricsService _metrics;

    public SessionCleanupService(
        ILogger<SessionCleanupService> logger,
        IOptions<DispatcherOptions> options,
        IServiceProvider serviceProvider,
        IMetricsService metrics)
    {
        _logger = logger;
        _options = options;
        _sessionManager = (SessionManager)serviceProvider.GetRequiredService<ISessionManager>();
        _metrics = metrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Session Cleanup Service starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.Value.SessionCleanupInterval, stoppingToken);

                _logger.LogInformation("Running session cleanup");
                
                _sessionManager.CleanupInactiveSessions(_options.Value.SessionTimeout);
                
                var activeCount = await _sessionManager.GetActiveSessionCountAsync();
                _metrics.RecordActiveSessions(activeCount);
                
                _logger.LogInformation("Session cleanup completed. Active sessions: {Count}", activeCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during session cleanup");
            }
        }
    }
}