namespace Orpheus.Services.Downloader.Youtube;

public interface IYouTubeDownloader
{
    Task<string?> DownloadAsync(string url);
    /// <summary>
    /// Download a video with known title to avoid redundant API calls for metadata.
    /// </summary>
    Task<string?> DownloadAsync(string url, string? knownTitle);
    Task<string?> GetVideoTitleAsync(string url);
    Task<string?> SearchAndGetFirstUrlAsync(string searchQuery);
    
    /// <summary>
    /// Search for a video and return both URL and metadata to avoid redundant API calls.
    /// This is more efficient than calling SearchAndGetFirstUrlAsync followed by GetVideoTitleAsync.
    /// </summary>
    Task<SearchResult?> SearchAndGetFirstResultAsync(string searchQuery);
}