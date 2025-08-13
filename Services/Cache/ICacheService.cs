namespace Orpheus.Services.Cache;

public interface ICacheService
{
    /// <summary>
    /// Checks if a song is cached by its unique identifier
    /// </summary>
    Task<bool> IsCachedAsync(string uniqueId);

    /// <summary>
    /// Gets a cached song by its unique identifier
    /// </summary>
    Task<CachedSong?> GetCachedSongAsync(string uniqueId);

    /// <summary>
    /// Adds a song to the cache
    /// </summary>
    Task<bool> AddToCacheAsync(string uniqueId, string title, string url, string filePath);

    /// <summary>
    /// Updates the last accessed time for a cached song
    /// </summary>
    Task UpdateLastAccessedAsync(string uniqueId);

    /// <summary>
    /// Gets cache statistics (total files, total size)
    /// </summary>
    Task<CacheStatistics> GetCacheStatisticsAsync();

    /// <summary>
    /// Manually triggers cache cleanup based on configured limits
    /// </summary>
    Task CleanupCacheAsync();

    /// <summary>
    /// Clears the entire cache
    /// </summary>
    Task ClearCacheAsync();
}

public class CacheStatistics
{
    public int TotalFiles { get; set; }
    public long TotalSizeBytes { get; set; }
    public int FilesEvicted { get; set; }
    public long SizeEvicted { get; set; }
}