using Microsoft.Extensions.Logging;
using Orpheus.Services.Cache;
using System.Text.RegularExpressions;

namespace Orpheus.Services.Downloader.Youtube;

public class CachedYouTubeDownloaderService : IYouTubeDownloader
{
    private readonly IYouTubeDownloader _baseDownloader;
    private readonly ICacheService _cacheService;
    private readonly ILogger<CachedYouTubeDownloaderService> _logger;
    private static readonly Regex YouTubeVideoIdRegex = new(@"(?:youtube\.com/watch\?v=|youtu\.be/|youtube\.com/embed/)([a-zA-Z0-9_-]{11})", RegexOptions.Compiled);

    public CachedYouTubeDownloaderService(
        IYouTubeDownloader baseDownloader,
        ICacheService cacheService,
        ILogger<CachedYouTubeDownloaderService> logger)
    {
        _baseDownloader = baseDownloader;
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task<string?> DownloadAsync(string url)
    {
        string? uniqueId = null;
        string downloadUrl = url; // The URL to actually download from

        // Handle search URLs with async resolution
        if (url.StartsWith("ytsearch"))
        {
            _logger.LogDebug("Resolving search URL: {Url}", url);
            
            // Extract search query and resolve to actual YouTube URL
            var searchQuery = url.Replace("ytsearch1:", "").Replace("ytsearch:", "");
            var resolvedUrl = await _baseDownloader.SearchAndGetFirstUrlAsync(searchQuery);
            
            if (string.IsNullOrWhiteSpace(resolvedUrl))
            {
                _logger.LogWarning("Could not resolve search URL: {Url}. Bypassing cache.", url);
                return await _baseDownloader.DownloadAsync(url);
            }
            
            downloadUrl = resolvedUrl; // Use resolved URL for download
            uniqueId = ExtractUniqueId(resolvedUrl); // Extract ID from resolved URL
            
            _logger.LogDebug("Resolved search URL {SearchUrl} to {ResolvedUrl} with ID {UniqueId}", 
                url, resolvedUrl, uniqueId ?? "null");
        }
        else
        {
            uniqueId = ExtractUniqueId(url);
        }

        if (string.IsNullOrEmpty(uniqueId))
        {
            _logger.LogWarning("Could not extract unique ID from URL: {Url}. Bypassing cache.", downloadUrl);
            return await _baseDownloader.DownloadAsync(downloadUrl);
        }

        _logger.LogDebug("Checking cache for video ID: {UniqueId} (URL: {Url})", uniqueId, downloadUrl);

        // Check if already cached
        var cachedSong = await _cacheService.GetCachedSongAsync(uniqueId);
        if (cachedSong != null)
        {
            _logger.LogInformation("Cache hit! Using cached file for {Title} ({UniqueId})", cachedSong.Title, uniqueId);
            await _cacheService.UpdateLastAccessedAsync(uniqueId);
            return cachedSong.FilePath;
        }

        _logger.LogDebug("Cache miss for {UniqueId}, downloading...", uniqueId);

        // Not cached, download using base downloader with resolved URL
        var filePath = await _baseDownloader.DownloadAsync(downloadUrl);
        if (string.IsNullOrEmpty(filePath))
        {
            _logger.LogWarning("Download failed for URL: {Url}", downloadUrl);
            return null;
        }

        // Get title for better cache metadata (use resolved URL for metadata)
        var title = await GetVideoTitleAsync(downloadUrl) ?? "Unknown Title";

        // Add to cache using resolved URL for better metadata
        var cacheSuccess = await _cacheService.AddToCacheAsync(uniqueId, title, downloadUrl, filePath);
        if (cacheSuccess)
        {
            _logger.LogInformation("Successfully cached downloaded file: {Title} ({UniqueId})", title, uniqueId);
        }
        else
        {
            _logger.LogWarning("Failed to add downloaded file to cache: {FilePath}", filePath);
        }

        return filePath;
    }

    public async Task<string?> GetVideoTitleAsync(string url)
    {
        var uniqueId = ExtractUniqueId(url);
        if (!string.IsNullOrEmpty(uniqueId))
        {
            _logger.LogDebug("Checking cache for video title: {UniqueId}", uniqueId);
            
            // Check if we have cached metadata for this video
            var cachedSong = await _cacheService.GetCachedSongAsync(uniqueId);
            if (cachedSong != null)
            {
                _logger.LogDebug("Cache hit for title! Using cached title: {Title} ({UniqueId})", cachedSong.Title, uniqueId);
                await _cacheService.UpdateLastAccessedAsync(uniqueId);
                return cachedSong.Title;
            }
            
            _logger.LogDebug("Cache miss for title {UniqueId}, fetching from source...", uniqueId);
        }
        
        // Not cached or no unique ID, delegate to base downloader for title fetching
        return await _baseDownloader.GetVideoTitleAsync(url);
    }

    public async Task<string?> SearchAndGetFirstUrlAsync(string searchQuery)
    {
        _logger.LogDebug("Searching for: {SearchQuery}", searchQuery);
        
        // Delegate to base downloader for search functionality
        var url = await _baseDownloader.SearchAndGetFirstUrlAsync(searchQuery);
        
        if (string.IsNullOrEmpty(url))
        {
            _logger.LogDebug("Search returned no results for: {SearchQuery}", searchQuery);
            return null;
        }
        
        // Check if the found URL corresponds to a cached video
        var uniqueId = ExtractUniqueId(url);
        if (!string.IsNullOrEmpty(uniqueId))
        {
            _logger.LogDebug("Search found URL {Url} with ID {UniqueId}, checking cache...", url, uniqueId);
            var cachedSong = await _cacheService.GetCachedSongAsync(uniqueId);
            if (cachedSong != null)
            {
                _logger.LogInformation("Search result for '{SearchQuery}' is already cached: {Title} ({UniqueId})", 
                    searchQuery, cachedSong.Title, uniqueId);
            }
            else
            {
                _logger.LogDebug("Search result for '{SearchQuery}' not cached, will be downloaded: {Url}", searchQuery, url);
            }
        }
        else
        {
            _logger.LogWarning("Could not extract unique ID from search result URL: {Url}", url);
        }
        
        return url;
    }

    /// <summary>
    /// Extracts a unique identifier from a YouTube URL
    /// </summary>
    private string? ExtractUniqueId(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        // Don't handle search URLs here - they should be resolved first
        if (url.StartsWith("ytsearch"))
            return null;

        // Extract YouTube video ID using regex
        var match = YouTubeVideoIdRegex.Match(url);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        // For other URLs, we could use the URL itself as the unique ID (hashed)
        // but for now, we'll only cache YouTube videos
        return null;
    }
}