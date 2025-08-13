namespace Orpheus.Services.Cache;

public class CachedSong
{
    public string UniqueId { get; }
    public string Title { get; set; }
    public string Url { get; }
    public string FilePath { get; }
    public DateTimeOffset CachedAt { get; internal set; }
    public DateTimeOffset LastAccessedAt { get; set; }
    public long FileSizeBytes { get; }

    public CachedSong(string uniqueId, string title, string url, string filePath, long fileSizeBytes)
    {
        UniqueId = uniqueId;
        Title = title;
        Url = url;
        FilePath = filePath;
        FileSizeBytes = fileSizeBytes;
        CachedAt = DateTimeOffset.UtcNow;
        LastAccessedAt = DateTimeOffset.UtcNow;
    }

    // Internal constructor for loading from database
    internal CachedSong(string uniqueId, string title, string url, string filePath, long fileSizeBytes, 
                       DateTimeOffset cachedAt, DateTimeOffset lastAccessedAt)
    {
        UniqueId = uniqueId;
        Title = title;
        Url = url;
        FilePath = filePath;
        FileSizeBytes = fileSizeBytes;
        CachedAt = cachedAt;
        LastAccessedAt = lastAccessedAt;
    }

    public void UpdateLastAccessed()
    {
        LastAccessedAt = DateTimeOffset.UtcNow;
    }
}