using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orpheus.Services.Cache;

namespace Orpheus.Services.Cache;

public class CacheCleanupService : BackgroundService
{
    private readonly ICacheService _cacheService;
    private readonly CacheConfiguration _config;
    private readonly ILogger<CacheCleanupService> _logger;

    public CacheCleanupService(
        ICacheService cacheService,
        CacheConfiguration config,
        ILogger<CacheCleanupService> logger)
    {
        _cacheService = cacheService;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.EnableAutomaticCleanup)
        {
            _logger.LogInformation("Automatic cache cleanup is disabled");
            return;
        }

        _logger.LogInformation("Cache cleanup service started. Cleanup interval: {Interval} minutes", 
            _config.CleanupIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(_config.CleanupIntervalMinutes), stoppingToken);
                
                if (stoppingToken.IsCancellationRequested)
                    break;

                _logger.LogDebug("Running automatic cache cleanup");
                await _cacheService.CleanupCacheAsync();
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelling
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during automatic cache cleanup");
                // Continue running even if one cleanup fails
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); // Wait 5 minutes before retry
            }
        }

        _logger.LogInformation("Cache cleanup service stopped");
    }
}