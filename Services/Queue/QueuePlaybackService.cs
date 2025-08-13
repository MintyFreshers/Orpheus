using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using NetCord.Gateway;
using Orpheus.Services.Downloader.Youtube;
using Orpheus.Services.VoiceClientController;

namespace Orpheus.Services.Queue;

public interface IQueuePlaybackService
{
    Task StartQueueProcessingAsync(Guild guild, GatewayClient client, ulong userId);
    Task StopQueueProcessingAsync();
    Task SkipCurrentSongAsync();
    bool IsProcessing { get; }
}

public class QueuePlaybackService : IQueuePlaybackService
{
    private readonly ISongQueueService _queueService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IYouTubeDownloader _downloader;
    private readonly IAudioPlaybackService _audioPlaybackService;
    private readonly ILogger<QueuePlaybackService> _logger;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _processingTask;
    private readonly object _lock = new();
    private TaskCompletionSource<bool>? _playbackCompletionSource;

    public QueuePlaybackService(
        ISongQueueService queueService,
        IServiceProvider serviceProvider,
        IYouTubeDownloader downloader,
        IAudioPlaybackService audioPlaybackService,
        ILogger<QueuePlaybackService> logger)
    {
        _queueService = queueService;
        _serviceProvider = serviceProvider;
        _downloader = downloader;
        _audioPlaybackService = audioPlaybackService;
        _logger = logger;

        // Subscribe to playback completion events
        _audioPlaybackService.PlaybackCompleted += OnPlaybackCompleted;
    }

    public bool IsProcessing
    {
        get
        {
            lock (_lock)
            {
                return _processingTask != null && !_processingTask.IsCompleted;
            }
        }
    }

    public async Task StartQueueProcessingAsync(Guild guild, GatewayClient client, ulong userId)
    {
        lock (_lock)
        {
            if (IsProcessing)
            {
                _logger.LogDebug("Queue processing is already running");
                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            _processingTask = ProcessQueueAsync(guild, client, userId, _cancellationTokenSource.Token);
        }

        _logger.LogInformation("Started queue processing for guild {GuildId}", guild.Id);
        await Task.CompletedTask;
    }

    public async Task StopQueueProcessingAsync()
    {
        Task? taskToWait = null;

        lock (_lock)
        {
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                taskToWait = _processingTask;
            }
        }

        if (taskToWait != null)
        {
            try
            {
                await taskToWait;
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelling
            }
        }

        var voiceClientController = _serviceProvider.GetRequiredService<IVoiceClientController>();
        await voiceClientController.StopPlaybackAsync();
        _queueService.SetCurrentSong(null);
        _logger.LogInformation("Stopped queue processing");
    }

    public async Task SkipCurrentSongAsync()
    {
        var voiceClientController = _serviceProvider.GetRequiredService<IVoiceClientController>();
        await voiceClientController.StopPlaybackAsync();
        _queueService.SetCurrentSong(null);
        _logger.LogInformation("Skipped current song");
    }

    private async Task ProcessQueueAsync(Guild guild, GatewayClient client, ulong userId, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var nextSong = _queueService.DequeueNext();
                if (nextSong == null)
                {
                    // Wait for songs to be added to the queue - reduced delay for better responsiveness
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }

                _queueService.SetCurrentSong(nextSong);
                _logger.LogInformation("Processing song from queue: {Title}", nextSong.Title);

                try
                {
                    // Wait for the song to be downloaded, but don't put it back in queue
                    await WaitForSongDownloadAsync(nextSong, cancellationToken);

                    if (string.IsNullOrWhiteSpace(nextSong.FilePath))
                    {
                        throw new InvalidOperationException($"Song file path is null after download: {nextSong.Title}");
                    }

                    _logger.LogInformation("Playing song: {Title} from file: {FilePath}", nextSong.Title, nextSong.FilePath);
                    _logger.LogDebug("File exists check: {FileExists}, File size: {FileSize} bytes", 
                        File.Exists(nextSong.FilePath), 
                        File.Exists(nextSong.FilePath) ? new FileInfo(nextSong.FilePath).Length : 0);
                    
                    _logger.LogDebug("Attempting to call VoiceClientController.PlayMp3Async...");
                    var voiceClientController = _serviceProvider.GetRequiredService<IVoiceClientController>();
                    var result = await voiceClientController.PlayMp3Async(guild, client, nextSong.RequestedByUserId, nextSong.FilePath);
                    _logger.LogInformation("Playback result: {Result}", result);
                    
                    if (result.Contains("Failed") || result.Contains("not found"))
                    {
                        throw new InvalidOperationException($"Audio playback failed: {result}");
                    }

                    // Wait for the song to finish playing
                    await WaitForSongCompletionAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error playing song: {Title}", nextSong.Title);
                }

                _queueService.SetCurrentSong(null);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Queue processing was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in queue processing");
        }
    }

    private async Task WaitForSongDownloadAsync(QueuedSong song, CancellationToken cancellationToken)
    {
        var maxWaitTime = TimeSpan.FromMinutes(2); // Max time to wait for download
        var checkInterval = TimeSpan.FromSeconds(1);
        var elapsed = TimeSpan.Zero;

        while (elapsed < maxWaitTime && !cancellationToken.IsCancellationRequested)
        {
            // Check if the song is ready (downloaded)
            if (!string.IsNullOrWhiteSpace(song.FilePath) && File.Exists(song.FilePath))
            {
                return; // Song is ready
            }

            await Task.Delay(checkInterval, cancellationToken);
            elapsed = elapsed.Add(checkInterval);
        }

        // If we reach here, download timed out
        throw new InvalidOperationException($"Song download timed out after {maxWaitTime}: {song.Title}");
    }

    private async Task WaitForSongCompletionAsync(CancellationToken cancellationToken)
    {
        // Create a new completion source for this song
        _playbackCompletionSource = new TaskCompletionSource<bool>();
        
        try
        {
            // Wait for either:
            // 1. Playback to complete naturally (via event)
            // 2. Song to be skipped/stopped (CurrentSong becomes null)
            // 3. Cancellation
            var checkInterval = TimeSpan.FromMilliseconds(500); // Reduced to 500ms for better responsiveness
            var maxWaitTime = TimeSpan.FromMinutes(10); // Max song length
            var elapsed = TimeSpan.Zero;

            while (elapsed < maxWaitTime && !cancellationToken.IsCancellationRequested)
            {
                // Check if playback completed
                if (_playbackCompletionSource.Task.IsCompleted)
                {
                    _logger.LogDebug("Song finished playing naturally");
                    return;
                }

                // Check if the current song was cleared (indicating a skip or stop)
                if (_queueService.CurrentSong == null)
                {
                    _logger.LogDebug("Song was skipped or stopped");
                    return;
                }

                await Task.Delay(checkInterval, cancellationToken);
                elapsed = elapsed.Add(checkInterval);
            }

            _logger.LogWarning("Song completion wait timed out after {MaxWaitTime}", maxWaitTime);
        }
        finally
        {
            _playbackCompletionSource = null;
        }
    }

    private void OnPlaybackCompleted()
    {
        _logger.LogDebug("Received playback completion event");
        _playbackCompletionSource?.TrySetResult(true);
    }
}