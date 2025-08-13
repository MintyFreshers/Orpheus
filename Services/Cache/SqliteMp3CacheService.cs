using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Data;

namespace Orpheus.Services.Cache;

public class SqliteMp3CacheService : ICacheService, IDisposable
{
    private readonly CacheConfiguration _config;
    private readonly ILogger<SqliteMp3CacheService> _logger;
    private readonly SqliteConnection _connection;
    private readonly object _lock = new();
    private readonly string _databasePath;

    public SqliteMp3CacheService(CacheConfiguration config, ILogger<SqliteMp3CacheService> logger)
    {
        _config = config;
        _logger = logger;
        
        // Ensure cache directory exists
        _logger.LogDebug("Creating cache directory: {CacheDirectory}", _config.CacheDirectory);
        Directory.CreateDirectory(_config.CacheDirectory);
        _logger.LogInformation("Cache directory ensured: {CacheDirectory}", _config.CacheDirectory);
        
        _databasePath = Path.Combine(_config.CacheDirectory, "cache.db");
        _logger.LogDebug("SQLite database path: {DatabasePath}", _databasePath);
        _connection = new SqliteConnection($"Data Source={_databasePath}");
        
        _ = Task.Run(InitializeDatabaseAsync);
    }

    private async Task InitializeDatabaseAsync()
    {
        try
        {
            await _connection.OpenAsync();
            
            // Create cache table if it doesn't exist
            const string createTableSql = @"
                CREATE TABLE IF NOT EXISTS cached_songs (
                    unique_id TEXT PRIMARY KEY,
                    title TEXT NOT NULL,
                    url TEXT NOT NULL,
                    file_path TEXT NOT NULL,
                    file_size_bytes INTEGER NOT NULL,
                    cached_at TEXT NOT NULL,
                    last_accessed_at TEXT NOT NULL
                );
                
                CREATE INDEX IF NOT EXISTS idx_last_accessed ON cached_songs(last_accessed_at);
            ";

            using var command = new SqliteCommand(createTableSql, _connection);
            await command.ExecuteNonQueryAsync();
            
            // Clean up entries for files that no longer exist
            await CleanupMissingFilesAsync();
            
            var cacheCount = await GetCacheCountAsync();
            _logger.LogInformation("SQLite cache initialized with {Count} cached songs", cacheCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize SQLite cache database");
            throw;
        }
    }

    public Task<bool> IsCachedAsync(string uniqueId)
    {
        try
        {
            lock (_lock)
            {
                const string sql = "SELECT file_path FROM cached_songs WHERE unique_id = @uniqueId";
                using var command = new SqliteCommand(sql, _connection);
                command.Parameters.AddWithValue("@uniqueId", uniqueId);
                
                var filePath = command.ExecuteScalar() as string;
                
                if (!string.IsNullOrEmpty(filePath))
                {
                    // Verify file still exists
                    if (File.Exists(filePath))
                    {
                        return Task.FromResult(true);
                    }
                    else
                    {
                        // File was deleted externally, remove from cache
                        RemoveCachedEntrySync(uniqueId);
                        _logger.LogWarning("Cached file no longer exists, removing from cache: {FilePath}", filePath);
                    }
                }
                
                return Task.FromResult(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if item is cached: {UniqueId}", uniqueId);
            return Task.FromResult(false);
        }
    }

    public Task<CachedSong?> GetCachedSongAsync(string uniqueId)
    {
        try
        {
            lock (_lock)
            {
                const string sql = @"
                    SELECT unique_id, title, url, file_path, file_size_bytes, cached_at, last_accessed_at 
                    FROM cached_songs 
                    WHERE unique_id = @uniqueId";
                
                using var command = new SqliteCommand(sql, _connection);
                command.Parameters.AddWithValue("@uniqueId", uniqueId);
                
                using var reader = command.ExecuteReader();
                
                if (reader.Read())
                {
                    var filePath = reader.GetString("file_path");
                    
                    // Verify file still exists
                    if (File.Exists(filePath))
                    {
                        var cachedSong = new CachedSong(
                            reader.GetString("unique_id"),
                            reader.GetString("title"),
                            reader.GetString("url"),
                            filePath,
                            reader.GetInt64("file_size_bytes"),
                            DateTimeOffset.Parse(reader.GetString("cached_at")),
                            DateTimeOffset.Parse(reader.GetString("last_accessed_at")));
                        
                        cachedSong.UpdateLastAccessed();
                        
                        // Update last accessed time in database
                        _ = Task.Run(() => UpdateLastAccessedAsync(uniqueId));
                        
                        _logger.LogDebug("Cache hit for {UniqueId}: {Title}", uniqueId, cachedSong.Title);
                        return Task.FromResult<CachedSong?>(cachedSong);
                    }
                    else
                    {
                        // File was deleted externally, remove from cache
                        RemoveCachedEntrySync(uniqueId);
                        _logger.LogWarning("Cached file no longer exists, removing from cache: {FilePath}", filePath);
                    }
                }
                
                _logger.LogDebug("Cache miss for {UniqueId}", uniqueId);
                return Task.FromResult<CachedSong?>(null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cached song: {UniqueId}", uniqueId);
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

        try
        {
            var fileInfo = new FileInfo(filePath);
            var now = DateTimeOffset.UtcNow;

            lock (_lock)
            {
                // Check if already cached
                const string checkSql = "SELECT COUNT(*) FROM cached_songs WHERE unique_id = @uniqueId";
                using var checkCommand = new SqliteCommand(checkSql, _connection);
                checkCommand.Parameters.AddWithValue("@uniqueId", uniqueId);
                
                var exists = Convert.ToInt32(checkCommand.ExecuteScalar()) > 0;
                if (exists)
                {
                    _logger.LogDebug("Song already cached: {UniqueId}", uniqueId);
                    return true;
                }

                const string insertSql = @"
                    INSERT INTO cached_songs (unique_id, title, url, file_path, file_size_bytes, cached_at, last_accessed_at)
                    VALUES (@uniqueId, @title, @url, @filePath, @fileSizeBytes, @cachedAt, @lastAccessedAt)";

                using var command = new SqliteCommand(insertSql, _connection);
                command.Parameters.AddWithValue("@uniqueId", uniqueId);
                command.Parameters.AddWithValue("@title", title);
                command.Parameters.AddWithValue("@url", url);
                command.Parameters.AddWithValue("@filePath", filePath);
                command.Parameters.AddWithValue("@fileSizeBytes", fileInfo.Length);
                command.Parameters.AddWithValue("@cachedAt", now.ToString("O"));
                command.Parameters.AddWithValue("@lastAccessedAt", now.ToString("O"));

                command.ExecuteNonQuery();
                
                _logger.LogInformation("Added to cache: {Title} ({UniqueId}), Size: {Size} bytes", 
                    title, uniqueId, fileInfo.Length);
            }

            // Check if we need to evict items
            await CleanupCacheAsync();
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding item to cache: {UniqueId}", uniqueId);
            return false;
        }
    }

    public Task UpdateLastAccessedAsync(string uniqueId)
    {
        try
        {
            lock (_lock)
            {
                const string sql = "UPDATE cached_songs SET last_accessed_at = @lastAccessedAt WHERE unique_id = @uniqueId";
                using var command = new SqliteCommand(sql, _connection);
                command.Parameters.AddWithValue("@uniqueId", uniqueId);
                command.Parameters.AddWithValue("@lastAccessedAt", DateTimeOffset.UtcNow.ToString("O"));
                
                var rowsAffected = command.ExecuteNonQuery();
                if (rowsAffected > 0)
                {
                    _logger.LogDebug("Updated last accessed time for {UniqueId}", uniqueId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating last accessed time: {UniqueId}", uniqueId);
        }
        
        return Task.CompletedTask;
    }

    public Task<CacheStatistics> GetCacheStatisticsAsync()
    {
        try
        {
            lock (_lock)
            {
                const string sql = "SELECT COUNT(*) as file_count, COALESCE(SUM(file_size_bytes), 0) as total_size FROM cached_songs";
                using var command = new SqliteCommand(sql, _connection);
                using var reader = command.ExecuteReader();
                
                if (reader.Read())
                {
                    return Task.FromResult(new CacheStatistics
                    {
                        TotalFiles = reader.GetInt32("file_count"),
                        TotalSizeBytes = reader.GetInt64("total_size")
                    });
                }
                
                return Task.FromResult(new CacheStatistics());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache statistics");
            return Task.FromResult(new CacheStatistics());
        }
    }

    public async Task CleanupCacheAsync()
    {
        try
        {
            var itemsToEvict = new List<CachedSong>();
            var stats = new CacheStatistics();

            // First, remove any cached items whose files no longer exist
            await CleanupMissingFilesAsync();

            lock (_lock)
            {
                // Get current statistics
                const string statsSql = "SELECT COUNT(*) as file_count, COALESCE(SUM(file_size_bytes), 0) as total_size FROM cached_songs";
                using var statsCommand = new SqliteCommand(statsSql, _connection);
                using var statsReader = statsCommand.ExecuteReader();
                
                int currentFiles = 0;
                long currentSize = 0;
                
                if (statsReader.Read())
                {
                    currentFiles = statsReader.GetInt32("file_count");
                    currentSize = statsReader.GetInt64("total_size");
                }
                statsReader.Close();

                // Check if we need to evict by file count
                if (_config.MaxFiles > 0 && currentFiles > _config.MaxFiles)
                {
                    var excessCount = currentFiles - _config.MaxFiles;
                    const string lruFilesSql = @"
                        SELECT unique_id, title, url, file_path, file_size_bytes, cached_at, last_accessed_at 
                        FROM cached_songs 
                        ORDER BY last_accessed_at ASC 
                        LIMIT @limit";
                    
                    using var command = new SqliteCommand(lruFilesSql, _connection);
                    command.Parameters.AddWithValue("@limit", excessCount);
                    using var reader = command.ExecuteReader();
                    
                    while (reader.Read())
                    {
                        var cachedSong = new CachedSong(
                            reader.GetString("unique_id"),
                            reader.GetString("title"),
                            reader.GetString("url"),
                            reader.GetString("file_path"),
                            reader.GetInt64("file_size_bytes"),
                            DateTimeOffset.Parse(reader.GetString("cached_at")),
                            DateTimeOffset.Parse(reader.GetString("last_accessed_at")));
                        
                        itemsToEvict.Add(cachedSong);
                    }
                    reader.Close();
                }

                // Check if we need to evict by size
                if (_config.MaxSizeBytes > 0 && currentSize > _config.MaxSizeBytes)
                {
                    var excessSize = currentSize - _config.MaxSizeBytes;
                    
                    // Get items not already marked for eviction, ordered by LRU
                    var evictedIds = itemsToEvict.Select(s => $"'{s.UniqueId}'").ToList();
                    var whereClause = evictedIds.Count > 0 ? $"WHERE unique_id NOT IN ({string.Join(",", evictedIds)})" : "";
                    
                    var lruSizeSql = $@"
                        SELECT unique_id, title, url, file_path, file_size_bytes, cached_at, last_accessed_at 
                        FROM cached_songs 
                        {whereClause}
                        ORDER BY last_accessed_at ASC";
                    
                    using var command = new SqliteCommand(lruSizeSql, _connection);
                    using var reader = command.ExecuteReader();
                    
                    long sizeToRemove = 0;
                    while (reader.Read() && sizeToRemove < excessSize)
                    {
                        var cachedSong = new CachedSong(
                            reader.GetString("unique_id"),
                            reader.GetString("title"),
                            reader.GetString("url"),
                            reader.GetString("file_path"),
                            reader.GetInt64("file_size_bytes"),
                            DateTimeOffset.Parse(reader.GetString("cached_at")),
                            DateTimeOffset.Parse(reader.GetString("last_accessed_at")));
                        
                        itemsToEvict.Add(cachedSong);
                        sizeToRemove += cachedSong.FileSizeBytes;
                    }
                    reader.Close();
                }

                // Remove evicted items from database
                foreach (var item in itemsToEvict)
                {
                    RemoveCachedEntrySync(item.UniqueId);
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
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cache cleanup");
        }
    }

    public async Task ClearCacheAsync()
    {
        var itemsToDelete = new List<string>();
        
        try
        {
            lock (_lock)
            {
                // Get all file paths
                const string sql = "SELECT file_path FROM cached_songs";
                using var command = new SqliteCommand(sql, _connection);
                using var reader = command.ExecuteReader();
                
                while (reader.Read())
                {
                    itemsToDelete.Add(reader.GetString("file_path"));
                }
                reader.Close();

                // Clear all entries from database
                const string deleteSql = "DELETE FROM cached_songs";
                using var deleteCommand = new SqliteCommand(deleteSql, _connection);
                deleteCommand.ExecuteNonQuery();
            }

            // Delete all cached files
            foreach (var filePath in itemsToDelete)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete cache file: {FilePath}", filePath);
                }
            }

            _logger.LogInformation("Cache cleared. Removed {Count} files", itemsToDelete.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cache");
        }
    }

    private async Task CleanupMissingFilesAsync()
    {
        try
        {
            var missingFiles = new List<string>();
            
            lock (_lock)
            {
                const string sql = "SELECT unique_id, file_path FROM cached_songs";
                using var command = new SqliteCommand(sql, _connection);
                using var reader = command.ExecuteReader();
                
                while (reader.Read())
                {
                    var uniqueId = reader.GetString("unique_id");
                    var filePath = reader.GetString("file_path");
                    
                    if (!File.Exists(filePath))
                    {
                        missingFiles.Add(uniqueId);
                    }
                }
                reader.Close();
                
                foreach (var uniqueId in missingFiles)
                {
                    RemoveCachedEntrySync(uniqueId);
                    _logger.LogInformation("Removed missing file from cache: {UniqueId}", uniqueId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during missing files cleanup");
        }
    }

    private void RemoveCachedEntrySync(string uniqueId)
    {
        try
        {
            const string sql = "DELETE FROM cached_songs WHERE unique_id = @uniqueId";
            using var command = new SqliteCommand(sql, _connection);
            command.Parameters.AddWithValue("@uniqueId", uniqueId);
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cached entry: {UniqueId}", uniqueId);
        }
    }

    private async Task<int> GetCacheCountAsync()
    {
        try
        {
            lock (_lock)
            {
                const string sql = "SELECT COUNT(*) FROM cached_songs";
                using var command = new SqliteCommand(sql, _connection);
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache count");
            return 0;
        }
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}