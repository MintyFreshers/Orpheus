using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Orpheus.Services.Cache;

public class Mp3CacheService : ICacheService
{
    private readonly CacheConfiguration _config;
    private readonly ILogger<Mp3CacheService> _logger;
    private readonly Dictionary<string, CachedSong> _cache = new();
    private readonly object _lock = new();
    private readonly string _metadataFilePath;

    public Mp3CacheService(CacheConfiguration config, ILogger<Mp3CacheService> logger)
    {
        _config = config;
        _logger = logger;
        _metadataFilePath = Path.Combine(_config.CacheDirectory, _config.MetadataFileName);
        
        // Ensure cache directory exists
        Directory.CreateDirectory(_config.CacheDirectory);
        
        // Load existing cache metadata
        _ = Task.Run(LoadCacheMetadataAsync);
    }

    public Task<bool> IsCachedAsync(string uniqueId)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(uniqueId, out var cachedSong))
            {
                // Verify file still exists
                if (File.Exists(cachedSong.FilePath))
                {
                    return Task.FromResult(true);
                }
                else
                {
                    // File was deleted externally, remove from cache
                    _cache.Remove(uniqueId);
                    _logger.LogWarning("Cached file no longer exists, removing from cache: {FilePath}", cachedSong.FilePath);
                    _ = Task.Run(SaveCacheMetadataAsync);
                }
            }
            
            return Task.FromResult(false);
        }
    }

    public Task<CachedSong?> GetCachedSongAsync(string uniqueId)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(uniqueId, out var cachedSong))
            {
                // Verify file still exists
                if (File.Exists(cachedSong.FilePath))
                {
                    cachedSong.UpdateLastAccessed();
                    _logger.LogDebug("Cache hit for {UniqueId}: {Title}", uniqueId, cachedSong.Title);
                    
                    // Save updated access time asynchronously
                    _ = Task.Run(SaveCacheMetadataAsync);
                    
                    return Task.FromResult<CachedSong?>(cachedSong);
                }
                else
                {
                    // File was deleted externally, remove from cache
                    _cache.Remove(uniqueId);
                    _logger.LogWarning("Cached file no longer exists, removing from cache: {FilePath}", cachedSong.FilePath);
                    _ = Task.Run(SaveCacheMetadataAsync);
                }
            }
            
            _logger.LogDebug("Cache miss for {UniqueId}", uniqueId);
            return Task.FromResult<CachedSong?>(null);
        }
    }

    public async Task<bool> AddToCacheAsync(string uniqueId, string title, string url, string filePath)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Cannot cache non-existent file: {FilePath}", filePath);
            return false;
        }

        var fileInfo = new FileInfo(filePath);
        var cachedSong = new CachedSong(uniqueId, title, url, filePath, fileInfo.Length);

        lock (_lock)
        {
            // Check if already cached
            if (_cache.ContainsKey(uniqueId))
            {
                _logger.LogDebug("Song already cached: {UniqueId}", uniqueId);
                return true;
            }

            _cache[uniqueId] = cachedSong;
            _logger.LogInformation("Added to cache: {Title} ({UniqueId}), Size: {Size} bytes", 
                title, uniqueId, fileInfo.Length);
        }

        // Check if we need to evict items
        await CleanupCacheAsync();
        
        // Save metadata
        await SaveCacheMetadataAsync();
        
        return true;
    }

    public Task UpdateLastAccessedAsync(string uniqueId)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(uniqueId, out var cachedSong))
            {
                cachedSong.UpdateLastAccessed();
                _logger.LogDebug("Updated last accessed time for {UniqueId}", uniqueId);
            }
        }
        
        // Save updated access time asynchronously
        _ = Task.Run(SaveCacheMetadataAsync);
        
        return Task.CompletedTask;
    }

    public Task<CacheStatistics> GetCacheStatisticsAsync()
    {
        lock (_lock)
        {
            var stats = new CacheStatistics
            {
                TotalFiles = _cache.Count,
                TotalSizeBytes = _cache.Values.Sum(c => c.FileSizeBytes)
            };
            
            return Task.FromResult(stats);
        }
    }

    public async Task CleanupCacheAsync()
    {
        var itemsToEvict = new List<CachedSong>();
        var stats = new CacheStatistics();

        lock (_lock)
        {
            // First, remove any cached items whose files no longer exist
            var toRemove = _cache.Where(kvp => !File.Exists(kvp.Value.FilePath)).ToList();
            foreach (var item in toRemove)
            {
                _cache.Remove(item.Key);
                _logger.LogInformation("Removed missing file from cache: {FilePath}", item.Value.FilePath);
            }

            var currentStats = new CacheStatistics
            {
                TotalFiles = _cache.Count,
                TotalSizeBytes = _cache.Values.Sum(c => c.FileSizeBytes)
            };

            // Check if we need to evict by file count
            if (_config.MaxFiles > 0 && currentStats.TotalFiles > _config.MaxFiles)
            {
                var excessCount = currentStats.TotalFiles - _config.MaxFiles;
                var lruItems = _cache.Values
                    .OrderBy(c => c.LastAccessedAt)
                    .Take(excessCount)
                    .ToList();
                
                itemsToEvict.AddRange(lruItems);
            }

            // Check if we need to evict by size
            if (_config.MaxSizeBytes > 0 && currentStats.TotalSizeBytes > _config.MaxSizeBytes)
            {
                var excessSize = currentStats.TotalSizeBytes - _config.MaxSizeBytes;
                var lruItems = _cache.Values
                    .OrderBy(c => c.LastAccessedAt)
                    .Where(c => !itemsToEvict.Contains(c)) // Don't double-add items
                    .ToList();

                long sizeToRemove = 0;
                foreach (var item in lruItems)
                {
                    if (sizeToRemove >= excessSize)
                        break;
                    
                    itemsToEvict.Add(item);
                    sizeToRemove += item.FileSizeBytes;
                }
            }

            // Remove evicted items from cache
            foreach (var item in itemsToEvict)
            {
                _cache.Remove(item.UniqueId);
                stats.FilesEvicted++;
                stats.SizeEvicted += item.FileSizeBytes;
            }
        }

        // Delete files and log evictions
        foreach (var item in itemsToEvict)
        {
            try
            {
                if (File.Exists(item.FilePath))
                {
                    File.Delete(item.FilePath);
                    _logger.LogInformation("Evicted from cache: {Title} ({UniqueId})", item.Title, item.UniqueId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete evicted cache file: {FilePath}", item.FilePath);
            }
        }

        if (stats.FilesEvicted > 0)
        {
            _logger.LogInformation("Cache cleanup completed. Evicted {FilesEvicted} files, {SizeEvicted} bytes", 
                stats.FilesEvicted, stats.SizeEvicted);
            
            await SaveCacheMetadataAsync();
        }
    }

    public async Task ClearCacheAsync()
    {
        var itemsToDelete = new List<CachedSong>();
        
        lock (_lock)
        {
            itemsToDelete.AddRange(_cache.Values);
            _cache.Clear();
        }

        // Delete all cached files
        foreach (var item in itemsToDelete)
        {
            try
            {
                if (File.Exists(item.FilePath))
                {
                    File.Delete(item.FilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete cache file: {FilePath}", item.FilePath);
            }
        }

        _logger.LogInformation("Cache cleared. Removed {Count} files", itemsToDelete.Count);
        await SaveCacheMetadataAsync();
    }

    private async Task LoadCacheMetadataAsync()
    {
        try
        {
            if (!File.Exists(_metadataFilePath))
            {
                _logger.LogDebug("No cache metadata file found, starting with empty cache");
                return;
            }

            var json = await File.ReadAllTextAsync(_metadataFilePath);
            var metadata = JsonSerializer.Deserialize<Dictionary<string, CacheMetadata>>(json);
            
            if (metadata == null)
            {
                _logger.LogWarning("Failed to deserialize cache metadata");
                return;
            }

            lock (_lock)
            {
                foreach (var kvp in metadata)
                {
                    // Only load if file still exists
                    if (File.Exists(kvp.Value.FilePath))
                    {
                        var cachedSong = new CachedSong(
                            kvp.Key,
                            kvp.Value.Title,
                            kvp.Value.Url,
                            kvp.Value.FilePath,
                            kvp.Value.FileSizeBytes)
                        {
                            LastAccessedAt = kvp.Value.LastAccessedAt
                        };
                        
                        _cache[kvp.Key] = cachedSong;
                    }
                }
            }

            _logger.LogInformation("Loaded {Count} cached songs from metadata", _cache.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load cache metadata");
        }
    }

    private async Task SaveCacheMetadataAsync()
    {
        try
        {
            Dictionary<string, CacheMetadata> metadata;
            
            lock (_lock)
            {
                metadata = _cache.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new CacheMetadata
                    {
                        Title = kvp.Value.Title,
                        Url = kvp.Value.Url,
                        FilePath = kvp.Value.FilePath,
                        FileSizeBytes = kvp.Value.FileSizeBytes,
                        CachedAt = kvp.Value.CachedAt,
                        LastAccessedAt = kvp.Value.LastAccessedAt
                    });
            }

            var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_metadataFilePath, json);
            
            _logger.LogDebug("Saved cache metadata for {Count} songs", metadata.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save cache metadata");
        }
    }

    private class CacheMetadata
    {
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public DateTimeOffset CachedAt { get; set; }
        public DateTimeOffset LastAccessedAt { get; set; }
    }
}