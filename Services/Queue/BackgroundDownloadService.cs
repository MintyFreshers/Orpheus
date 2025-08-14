using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orpheus.Services.Downloader.Youtube;

namespace Orpheus.Services.Queue;

public interface IBackgroundDownloadService
{
    Task StartAsync();
    Task StopAsync();
    Task TriggerImmediateProcessingAsync(QueuedSong song);
}

public class BackgroundDownloadService : BackgroundService, IBackgroundDownloadService
{
    private readonly ISongQueueService _queueService;
    private readonly IYouTubeDownloader _downloader;
    private readonly IMessageUpdateService _messageUpdateService;
    private readonly ILogger<BackgroundDownloadService> _logger;
    private readonly HashSet<string> _downloadingUrls = new();
    private readonly HashSet<string> _fetchingMetadataUrls = new();
    private readonly Dictionary<string, int> _failedDownloadCounts = new(); // Track failed download attempts
    private readonly Dictionary<string, DateTime> _lastFailedAttempt = new(); // Track when last failure occurred
    private readonly object _downloadingLock = new();
    private readonly object _metadataLock = new();
    private readonly object _failedDownloadsLock = new();
    private readonly SemaphoreSlim _downloadSemaphore = new(3); // Max 3 concurrent downloads
    private readonly SemaphoreSlim _metadataSemaphore = new(5); // Max 5 concurrent metadata fetches

    // Configuration for retry behavior
    private const int MaxRetryAttempts = 3;
    private static readonly TimeSpan RetryBackoffPeriod = TimeSpan.FromMinutes(5);

    public BackgroundDownloadService(
        ISongQueueService queueService,
        IYouTubeDownloader downloader,
        IMessageUpdateService messageUpdateService,
        ILogger<BackgroundDownloadService> logger)
    {
        _queueService = queueService;
        _downloader = downloader;
        _messageUpdateService = messageUpdateService;
        _logger = logger;
        
        // Subscribe to song added events for immediate processing
        _queueService.SongAdded += OnSongAdded;
    }

    public async Task StartAsync()
    {
        await base.StartAsync(CancellationToken.None);
    }

    public async Task StopAsync()
    {
        await base.StopAsync(CancellationToken.None);
    }

    public Task TriggerImmediateProcessingAsync(QueuedSong song)
    {
        try
        {
            // Process metadata immediately for newly added song
            if (NeedsMetadata(song))
            {
                _ = Task.Run(async () => await FetchMetadataAsync(song, CancellationToken.None));
            }
            
            // Also start download if needed (but metadata has higher priority for follow-up messages)
            if (NeedsDownload(song))
            {
                _ = Task.Run(async () => await DownloadSongAsync(song, CancellationToken.None));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in immediate processing for song: {Title}", song.Title);
        }
        
        return Task.CompletedTask;
    }

    private void OnSongAdded(QueuedSong song)
    {
        // Fire and forget immediate processing
        _ = Task.Run(async () => await TriggerImmediateProcessingAsync(song));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background download service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessQueueDownloads(stoppingToken);
                
                // Wait before next check - reduced interval as immediate processing handles new songs
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelling
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in background download service");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation("Background download service stopped");
    }

    private async Task ProcessQueueDownloads(CancellationToken cancellationToken)
    {
        var queue = _queueService.GetQueue();
        var currentSong = _queueService.CurrentSong;
        
        var downloadTasks = new List<Task>();
        var metadataTasks = new List<Task>();
        
        // Process current song first if it needs downloading or metadata
        if (currentSong != null)
        {
            if (NeedsDownload(currentSong))
            {
                downloadTasks.Add(DownloadSongAsync(currentSong, cancellationToken));
            }
            else if (NeedsMetadata(currentSong))
            {
                metadataTasks.Add(FetchMetadataAsync(currentSong, cancellationToken));
            }
        }

        // Then process songs in queue
        foreach (var song in queue)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (NeedsDownload(song))
            {
                downloadTasks.Add(DownloadSongAsync(song, cancellationToken));
            }
            else if (NeedsMetadata(song))
            {
                metadataTasks.Add(FetchMetadataAsync(song, cancellationToken));
            }
        }

        // Start all tasks concurrently
        var allTasks = downloadTasks.Concat(metadataTasks);
        if (allTasks.Any())
        {
            await Task.WhenAll(allTasks);
        }
    }

    private bool NeedsDownload(QueuedSong song)
    {
        // Don't download if already downloaded
        if (!string.IsNullOrWhiteSpace(song.FilePath) && File.Exists(song.FilePath))
            return false;

        // Don't download if already downloading
        lock (_downloadingLock)
        {
            if (_downloadingUrls.Contains(song.Url))
                return false;
        }

        // Don't retry downloads that have failed too many times
        if (ShouldSkipFailedDownload(song.Url))
            return false;

        return true;
    }

    private bool ShouldSkipFailedDownload(string url)
    {
        lock (_failedDownloadsLock)
        {
            if (!_failedDownloadCounts.ContainsKey(url))
                return false;

            var failedCount = _failedDownloadCounts[url];
            if (failedCount < MaxRetryAttempts)
                return false;

            // Check if enough time has passed since last failure to allow retry
            if (_lastFailedAttempt.TryGetValue(url, out var lastFailure))
            {
                var timeSinceLastFailure = DateTime.UtcNow - lastFailure;
                if (timeSinceLastFailure >= RetryBackoffPeriod)
                {
                    _logger.LogInformation("Resetting retry count for URL after backoff period: {Url}", url);
                    _failedDownloadCounts[url] = 0;
                    _lastFailedAttempt.Remove(url);
                    return false;
                }
            }

            _logger.LogDebug("Skipping download for URL that has failed {FailedCount} times: {Url}", failedCount, url);
            return true;
        }
    }

    private void RecordFailedDownload(string url)
    {
        lock (_failedDownloadsLock)
        {
            _failedDownloadCounts[url] = _failedDownloadCounts.GetValueOrDefault(url, 0) + 1;
            _lastFailedAttempt[url] = DateTime.UtcNow;
            
            var failedCount = _failedDownloadCounts[url];
            _logger.LogWarning("Recorded failed download attempt {FailedCount}/{MaxAttempts} for URL: {Url}", 
                failedCount, MaxRetryAttempts, url);
        }
    }

    private void RecordSuccessfulDownload(string url)
    {
        lock (_failedDownloadsLock)
        {
            if (_failedDownloadCounts.ContainsKey(url))
            {
                _logger.LogDebug("Clearing failed download history for successful download: {Url}", url);
                _failedDownloadCounts.Remove(url);
                _lastFailedAttempt.Remove(url);
            }
        }
    }

    private bool NeedsMetadata(QueuedSong song)
    {
        // Need metadata if title is still generic or a search placeholder
        if (song.Title != "YouTube Video" && song.Title != "Audio Track" && !song.Title.StartsWith("Found: "))
            return false;

        // Don't fetch if already fetching
        lock (_metadataLock)
        {
            return !_fetchingMetadataUrls.Contains(song.Url);
        }
    }

    private async Task FetchMetadataAsync(QueuedSong song, CancellationToken cancellationToken)
    {
        // Check if we should fetch metadata
        lock (_metadataLock)
        {
            if (_fetchingMetadataUrls.Contains(song.Url))
            {
                _logger.LogDebug("Metadata already being fetched for: {Url}", song.Url);
                return;
            }
            _fetchingMetadataUrls.Add(song.Url);
        }

        await _metadataSemaphore.WaitAsync(cancellationToken);
        
        try
        {
            _logger.LogDebug("Fetching metadata for: {Url}, current title: '{CurrentTitle}'", song.Url, song.Title);
            
            var actualTitle = await _downloader.GetVideoTitleAsync(song.Url);
            _logger.LogDebug("Retrieved title for URL {Url}: '{Title}'", song.Url, actualTitle ?? "(null)");
            
            if (!string.IsNullOrWhiteSpace(actualTitle))
            {
                var oldTitle = song.Title;
                song.Title = actualTitle;
                _logger.LogInformation("Updated song title from '{OldTitle}' to '{NewTitle}'", oldTitle, actualTitle);
                
                // Send follow-up message with real title
                _logger.LogDebug("Sending title update for song ID: {SongId}", song.Id);
                await _messageUpdateService.SendSongTitleUpdateAsync(song.Id, actualTitle);
                _logger.LogDebug("Title update sent successfully");
            }
            else
            {
                _logger.LogWarning("Retrieved empty or null title for URL: {Url}", song.Url);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching metadata for song: {Url}", song.Url);
        }
        finally
        {
            _metadataSemaphore.Release();
            lock (_metadataLock)
            {
                _fetchingMetadataUrls.Remove(song.Url);
            }
        }
    }

    private async Task DownloadSongAsync(QueuedSong song, CancellationToken cancellationToken)
    {
        // Check if we should download
        lock (_downloadingLock)
        {
            if (_downloadingUrls.Contains(song.Url))
                return;
            _downloadingUrls.Add(song.Url);
        }

        await _downloadSemaphore.WaitAsync(cancellationToken);

        try
        {
            _logger.LogDebug("Background downloading: {Title}", song.Title);
            
            // Pass the known title to avoid redundant API calls when the title is not a placeholder
            string? knownTitle = null;
            if (!IsPlaceholderTitle(song.Title))
            {
                knownTitle = song.Title;
                _logger.LogDebug("Using known title from search results: {Title}", knownTitle);
            }
            
            var filePath = await _downloader.DownloadAsync(song.Url, knownTitle);
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                song.FilePath = filePath;
                _logger.LogDebug("Background download completed: {Title} -> {FilePath}, File exists: {FileExists}", 
                    song.Title, filePath, File.Exists(filePath));
                RecordSuccessfulDownload(song.Url);
            }
            else
            {
                _logger.LogWarning("Background download failed: {Title} - returned null/empty path", song.Title);
                RecordFailedDownload(song.Url);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading song: {Title}", song.Title);
            RecordFailedDownload(song.Url);
        }
        finally
        {
            _downloadSemaphore.Release();
            lock (_downloadingLock)
            {
                _downloadingUrls.Remove(song.Url);
            }
        }
    }

    public override void Dispose()
    {
        // Unsubscribe from events to prevent memory leaks
        _queueService.SongAdded -= OnSongAdded;
        
        _downloadSemaphore?.Dispose();
        _metadataSemaphore?.Dispose();
        base.Dispose();
    }

    private static bool IsPlaceholderTitle(string title)
    {
        return title == "YouTube Video" || title == "Audio Track" || title.StartsWith("Found: ");
    }
}