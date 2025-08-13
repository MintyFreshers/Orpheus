namespace Orpheus.Services.Downloader.Youtube;

/// <summary>
/// Represents the result of a YouTube search, containing both the URL and metadata
/// to avoid making separate API calls for title information.
/// </summary>
public class SearchResult
{
    public string Url { get; }
    public string? Title { get; }
    public string? VideoId { get; }

    public SearchResult(string url, string? title = null, string? videoId = null)
    {
        Url = url;
        Title = title;
        VideoId = videoId;
    }
}