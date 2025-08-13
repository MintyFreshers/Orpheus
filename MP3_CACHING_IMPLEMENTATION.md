# MP3 Caching Strategy Implementation

## Overview

This implementation adds a comprehensive MP3 caching strategy to the Orpheus Discord bot that prevents re-downloading frequently played songs while managing storage efficiently through configurable limits and LRU eviction.

## Architecture

### Core Components

1. **CachedSong** (`Services/Cache/CachedSong.cs`)
   - Data model for cached song metadata
   - Stores unique ID (YouTube video ID), title, URL, file path, timestamps, and file size
   - Tracks last accessed time for LRU eviction

2. **ICacheService** (`Services/Cache/ICacheService.cs`)
   - Interface defining cache operations
   - Methods for checking, retrieving, adding, and managing cached songs
   - Cache statistics and cleanup operations

3. **Mp3CacheService** (`Services/Cache/Mp3CacheService.cs`)
   - Thread-safe implementation of the cache service
   - LRU eviction policy based on configurable file count and size limits
   - Persistent metadata storage in JSON format
   - Automatic cleanup of orphaned cache entries

4. **CacheConfiguration** (`Services/Cache/CacheConfiguration.cs`)
   - Configuration class for cache limits and behavior
   - Default: 1GB total size, 100 files maximum
   - Configurable cleanup intervals and automatic cleanup toggle

5. **CachedYouTubeDownloaderService** (`Services/Downloader/Youtube/CachedYouTubeDownloaderService.cs`)
   - Wrapper around the existing YouTube downloader
   - Checks cache before downloading, extracts YouTube video IDs as unique identifiers
   - Transparent integration - no changes needed to existing code

6. **CacheCleanupService** (`Services/Cache/CacheCleanupService.cs`)
   - Background service for automatic cache maintenance
   - Runs periodic cleanup based on configuration
   - Handles cache size limits and file count limits

7. **CacheInfo Commands** (`Commands/CacheInfo.cs`)
   - User-facing slash commands: `/cacheinfo` and `/clearcache`
   - View cache statistics and manually clear cache
   - Admin-level permissions for cache clearing

## Key Features

### 🎵 Duplicate Prevention
- Uses YouTube video IDs as unique identifiers
- Automatically detects and serves cached files for repeated requests
- Logs cache hits/misses for monitoring

### 📊 LRU Eviction Policy
- Evicts least recently used files when limits are exceeded
- Considers both file count and total size limits
- Maintains access timestamps for accurate LRU ordering

### 🔒 Thread-Safe Operations
- All cache operations protected with proper locking
- Concurrent access from multiple Discord commands supported
- Asynchronous file operations to avoid blocking

### 💾 Persistent Metadata
- Cache metadata stored in JSON format (`cache_metadata.json`)
- Survives bot restarts
- Automatically removes orphaned entries for deleted files

### 🧹 Automatic Cleanup
- Background service runs periodic maintenance
- Configurable cleanup intervals (default: 60 minutes)
- Can be disabled for manual-only cleanup

### 📈 Monitoring & Management
- `/cacheinfo` command shows cache statistics
- `/clearcache` command for manual cache clearing
- Comprehensive logging of cache operations

## Configuration

Default configuration (can be customized in `CacheConfiguration`):

```csharp
MaxFiles = 100;                    // Maximum cached files
MaxSizeBytes = 1GB;               // Maximum total cache size
CacheDirectory = "Downloads";      // Cache storage directory
EnableAutomaticCleanup = true;     // Auto cleanup enabled
CleanupIntervalMinutes = 60;       // Cleanup every hour
```

## Integration

### Dependency Injection Setup
The cache system is registered in `Program.cs`:

```csharp
// Cache services
services.AddSingleton<CacheConfiguration>();
services.AddSingleton<ICacheService, Mp3CacheService>();
services.AddHostedService<CacheCleanupService>();

// Cached downloader wrapping base downloader
services.AddSingleton<YouTubeDownloaderService>(); // Base downloader
services.AddSingleton<IYouTubeDownloader>(provider =>
{
    var baseDownloader = provider.GetRequiredService<YouTubeDownloaderService>();
    var cacheService = provider.GetRequiredService<ICacheService>();
    var logger = provider.GetRequiredService<ILogger<CachedYouTubeDownloaderService>>();
    return new CachedYouTubeDownloaderService(baseDownloader, cacheService, logger);
});
```

### Transparent Operation
- No changes required to existing queue, playback, or command code
- Existing `/play` commands automatically benefit from caching
- Cache operates transparently in the background

## Usage Examples

### User Commands
```
/cacheinfo           # View cache statistics
/clearcache          # Clear entire cache (admin only)
```

### Developer Usage
```csharp
// Cache service can be injected into any service
var cachedSong = await _cacheService.GetCachedSongAsync(videoId);
if (cachedSong != null)
{
    // Use cached file
    return cachedSong.FilePath;
}
```

## Benefits

1. **Performance**: Eliminates redundant downloads of popular songs
2. **Bandwidth**: Reduces YouTube API calls and download bandwidth
3. **Storage**: Intelligent storage management with configurable limits
4. **Reliability**: Transparent fallback to original downloader if cache fails
5. **Monitoring**: Full visibility into cache performance and usage

## Minimal Impact Design

This implementation was designed with minimal changes in mind:
- Extends existing interfaces rather than replacing them
- No breaking changes to existing functionality
- Additive approach - cache can be disabled by removing registration
- Preserves all existing bot capabilities while adding new benefits

The cache system operates as a transparent layer that enhances performance without affecting the user experience or requiring changes to existing workflows.