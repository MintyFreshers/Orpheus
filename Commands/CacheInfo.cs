using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Orpheus.Services.Cache;

namespace Orpheus.Commands;

public class CacheInfo : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly ICacheService _cacheService;
    private readonly ILogger<CacheInfo> _logger;

    public CacheInfo(ICacheService cacheService, ILogger<CacheInfo> logger)
    {
        _cacheService = cacheService;
        _logger = logger;
    }

    [SlashCommand("cacheinfo", "View MP3 cache statistics", Contexts = [InteractionContextType.Guild])]
    public async Task HandleCacheInfoAsync()
    {
        try
        {
            _logger.LogInformation("Cache info command invoked by user {UserId}", Context.User.Id);

            var stats = await _cacheService.GetCacheStatisticsAsync();
            
            var sizeMB = stats.TotalSizeBytes / (1024.0 * 1024.0);
            
            var response = $"**🗄️ MP3 Cache Statistics**\n" +
                          $"📁 **Cached Files:** {stats.TotalFiles}\n" +
                          $"💾 **Total Size:** {sizeMB:F2} MB ({stats.TotalSizeBytes:N0} bytes)\n" +
                          $"🔄 **Status:** Active caching enabled";

            if (stats.FilesEvicted > 0)
            {
                var evictedSizeMB = stats.SizeEvicted / (1024.0 * 1024.0);
                response += $"\n📤 **Last Cleanup:** {stats.FilesEvicted} files evicted ({evictedSizeMB:F2} MB)";
            }

            await RespondAsync(InteractionCallback.Message(response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache statistics");
            await RespondAsync(InteractionCallback.Message("❌ Failed to retrieve cache statistics."));
        }
    }

    [SlashCommand("clearcache", "Clear the MP3 cache (Admin only)", Contexts = [InteractionContextType.Guild])]
    public async Task HandleClearCacheAsync()
    {
        try
        {
            _logger.LogInformation("Cache clear command invoked by user {UserId}", Context.User.Id);

            var statsBefore = await _cacheService.GetCacheStatisticsAsync();
            
            await _cacheService.ClearCacheAsync();
            
            var sizeMB = statsBefore.TotalSizeBytes / (1024.0 * 1024.0);
            
            var response = $"✅ **Cache Cleared Successfully**\n" +
                          $"🗑️ **Removed:** {statsBefore.TotalFiles} files ({sizeMB:F2} MB)";

            await RespondAsync(InteractionCallback.Message(response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cache");
            await RespondAsync(InteractionCallback.Message("❌ Failed to clear cache."));
        }
    }
}