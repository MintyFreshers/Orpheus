using Microsoft.Extensions.Logging;
using NetCord.Gateway.Voice;
using System.Diagnostics;

namespace Orpheus.Services.VoiceClientController;

public class AudioPlaybackService : IAudioPlaybackService
{
    private readonly ILogger<AudioPlaybackService> _logger;
    private Process? _currentFfmpegProcess;
    private readonly object _lock = new();
    private bool _isDucked = false;
    private const float DuckedVolumeMultiplier = 0.2f; // Reduce to 20% volume when ducked
    
    // Track current playback state for dynamic volume changes
    private string? _currentFilePath;
    private OpusEncodeStream? _currentOutputStream;
    private CancellationTokenSource? _currentCancellationTokenSource;

    public event Action? PlaybackCompleted;
    public event Action<bool>? OnDuckingChanged;

    public AudioPlaybackService(ILogger<AudioPlaybackService> logger)
    {
        _logger = logger;
    }

    public async Task PlayMp3ToStreamAsync(string filePath, OpusEncodeStream outputStream, CancellationToken cancellationToken = default)
    {
        await StopPlaybackAsync();
        await Task.Delay(100, cancellationToken);

        LogResourceUsage("Before FFMPEG start");

        // Store current playback state for dynamic volume changes
        lock (_lock)
        {
            _currentFilePath = filePath;
            _currentOutputStream = outputStream;
            _currentCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        await Task.Run(async () =>
        {
            // Set high priority for audio streaming thread
            try
            {
                Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
                _logger.LogDebug("Set audio streaming thread priority to AboveNormal");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set streaming thread priority");
            }

            _logger.LogDebug("Preparing to start FFMPEG for file: {FilePath}", filePath);

            // Apply ducking volume if currently ducked
            float volumeMultiplier = 1.0f;
            lock (_lock)
            {
                if (_isDucked)
                {
                    volumeMultiplier = DuckedVolumeMultiplier;
                    _logger.LogDebug("Applying ducked volume ({Volume}x) for main audio playback", volumeMultiplier);
                }
            }

            var startInfo = CreateFfmpegProcessStartInfo(filePath, volumeMultiplier);

            try
            {
                var ffmpeg = new Process { StartInfo = startInfo };
                lock (_lock)
                {
                    _currentFfmpegProcess = ffmpeg;
                }

                ffmpeg.Start();
                SetProcessPriority(ffmpeg);

                _logger.LogInformation("FFMPEG process started for file: {FilePath}", filePath);

                var stderrTask = ffmpeg.StandardError.ReadToEndAsync();

                // Use larger buffer size for smoother streaming (64KB instead of default 4KB)
                var bufferSize = 65536;
                
                CancellationToken combinedToken;
                lock (_lock)
                {
                    combinedToken = _currentCancellationTokenSource?.Token ?? cancellationToken;
                }
                
                await ffmpeg.StandardOutput.BaseStream.CopyToAsync(outputStream, bufferSize, combinedToken);
                await outputStream.FlushAsync(combinedToken);

                _logger.LogInformation("Finished streaming audio for file: {FilePath}", filePath);

                await ffmpeg.WaitForExitAsync(combinedToken);

                await HandleFfmpegCompletion(ffmpeg, stderrTask, filePath);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Audio playback was cancelled for file: {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while streaming audio for file: {FilePath}", filePath);
                throw;
            }
            finally
            {
                lock (_lock)
                {
                    _currentFfmpegProcess = null;
                    // Keep current state for potential restart, don't clear it here
                }
                LogResourceUsage("After FFMPEG playback");
            }
        }, cancellationToken);
    }

    public async Task PlayOverlayMp3Async(string filePath, OpusEncodeStream outputStream, CancellationToken cancellationToken = default)
    {
        await PlayOverlayMp3Async(filePath, outputStream, 1.0f, cancellationToken);
    }

    public async Task PlayOverlayMp3Async(string filePath, OpusEncodeStream outputStream, float volumeMultiplier, CancellationToken cancellationToken = default)
    {
        // Don't stop current playback for overlay sounds - just play on top
        _logger.LogDebug("Starting overlay playback for file: {FilePath}", filePath);

        await Task.Run(async () =>
        {
            // Set high priority for audio streaming thread
            try
            {
                Thread.CurrentThread.Priority = ThreadPriority.AboveNormal; // Higher priority for overlay sounds
                _logger.LogDebug("Set overlay audio streaming thread priority to AboveNormal");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set overlay streaming thread priority");
            }

            _logger.LogDebug("Preparing to start FFMPEG for overlay file: {FilePath} with volume: {Volume}x", filePath, volumeMultiplier);

            var startInfo = CreateFfmpegProcessStartInfo(filePath, volumeMultiplier);

            try
            {
                var ffmpeg = new Process { StartInfo = startInfo };
                ffmpeg.Start();
                SetProcessPriority(ffmpeg);

                _logger.LogInformation("FFMPEG process started for overlay file: {FilePath}", filePath);

                var stderrTask = ffmpeg.StandardError.ReadToEndAsync();

                // Use smaller buffer size for overlay audio to reduce latency
                var bufferSize = 16384;
                await ffmpeg.StandardOutput.BaseStream.CopyToAsync(outputStream, bufferSize, cancellationToken);
                await outputStream.FlushAsync(cancellationToken);

                _logger.LogInformation("Finished streaming overlay audio for file: {FilePath}", filePath);

                await ffmpeg.WaitForExitAsync(cancellationToken);

                var stderr = await stderrTask;
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    _logger.LogWarning("FFMPEG stderr for overlay file {FilePath}: {Stderr}", filePath, stderr);
                }

                if (ffmpeg.ExitCode != 0)
                {
                    _logger.LogWarning("FFMPEG exited with non-zero code {ExitCode} for overlay file: {FilePath}", ffmpeg.ExitCode, filePath);
                }
                else
                {
                    _logger.LogDebug("FFMPEG overlay completed successfully for file: {FilePath}", filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while streaming overlay audio for file: {FilePath}", filePath);
                throw;
            }
        }, cancellationToken);
    }

    public async Task PlayDuckedOverlayMp3Async(string filePath, OpusEncodeStream outputStream, CancellationToken cancellationToken = default)
    {
        await PlayDuckedOverlayMp3Async(filePath, outputStream, 1.0f, cancellationToken);
    }

    public async Task PlayDuckedOverlayMp3Async(string filePath, OpusEncodeStream outputStream, float volumeMultiplier, CancellationToken cancellationToken = default)
    {
        // New approach: Stop background music, play acknowledge sound, then resume background at ducked volume
        _logger.LogDebug("Starting ducked overlay playback for file: {FilePath} with volume: {Volume}x", filePath, volumeMultiplier);
        
        // Step 1: Store current playback state before stopping it
        string? backgroundFile;
        OpusEncodeStream? backgroundStream;
        
        lock (_lock)
        {
            backgroundFile = _currentFilePath;
            backgroundStream = _currentOutputStream;
        }
        
        // Step 2: Stop background music temporarily
        if (backgroundFile != null && backgroundStream != null && File.Exists(backgroundFile))
        {
            _logger.LogDebug("Temporarily stopping background music for acknowledge sound");
            await StopPlaybackAsync();
            await Task.Delay(50); // Brief pause for clean stop
        }
        
        // Step 3: Play acknowledge sound at specified volume
        _logger.LogDebug("Playing acknowledge sound at {Volume}x volume", volumeMultiplier);
        await PlayOverlayMp3Async(filePath, outputStream, volumeMultiplier, cancellationToken);
        
        // Step 4: Enable ducking for background music
        SetDucking(true);
        
        // Step 5: Resume background music at ducked volume (if there was background music)
        if (backgroundFile != null && backgroundStream != null && File.Exists(backgroundFile))
        {
            _logger.LogDebug("Resuming background music at ducked volume");
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(100); // Brief pause to ensure acknowledge sound completes
                    await PlayMp3ToStreamAsync(backgroundFile, backgroundStream);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to resume background music after acknowledge sound");
                }
            });
        }
        
        _logger.LogDebug("Ducked overlay playback completed for file: {FilePath} (background music will continue at ducked volume)", filePath);
    }

    public void SetDucking(bool enabled)
    {
        lock (_lock)
        {
            if (_isDucked == enabled)
                return;
                
            _isDucked = enabled;
            _logger.LogInformation("Audio ducking {Status} - background music volume {VolumeDescription}", 
                enabled ? "enabled" : "disabled",
                enabled ? $"reduced to {DuckedVolumeMultiplier * 100:F0}%" : "restored to 100%");
            
            // If disabling ducking and there's active background music, restart it at full volume
            if (!enabled)
            {
                _ = Task.Run(async () => await RestoreFullVolumePlaybackAsync());
            }
            
            // Fire the ducking changed event for external listeners
            OnDuckingChanged?.Invoke(enabled);
        }
    }

    private async Task RestoreFullVolumePlaybackAsync()
    {
        try
        {
            string? currentFile;
            OpusEncodeStream? currentStream;
            
            lock (_lock)
            {
                currentFile = _currentFilePath;
                currentStream = _currentOutputStream;
            }
            
            // Only restart if there's active background music that needs volume restoration
            if (currentFile != null && currentStream != null && File.Exists(currentFile))
            {
                _logger.LogDebug("Restoring background music to full volume for file: {FilePath}", currentFile);
                
                // Stop current ducked playback
                await StopPlaybackAsync();
                await Task.Delay(100); // Brief pause for clean stop
                
                // Resume at full volume
                await PlayMp3ToStreamAsync(currentFile, currentStream);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error in RestoreFullVolumePlaybackAsync");
        }
    }

    public Task StopPlaybackAsync()
    {
        lock (_lock)
        {
            // Cancel current playback
            _currentCancellationTokenSource?.Cancel();
            _currentCancellationTokenSource?.Dispose();
            _currentCancellationTokenSource = null;
            
            // Clear current playback state
            _currentFilePath = null;
            _currentOutputStream = null;
            
            if (_currentFfmpegProcess != null && !_currentFfmpegProcess.HasExited)
            {
                try
                {
                    _logger.LogInformation("Stopping FFMPEG playback process");
                    _currentFfmpegProcess.Kill(true);
                    _currentFfmpegProcess = null;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to stop FFMPEG process");
                }
            }
        }
        return Task.CompletedTask;
    }

    private ProcessStartInfo CreateFfmpegProcessStartInfo(string filePath, float volumeMultiplier = 1.0f)
    {
        var startInfo = new ProcessStartInfo("ffmpeg")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Directory.GetCurrentDirectory()
        };

        var arguments = startInfo.ArgumentList;
        
        // Input options must come BEFORE -i and the input file
        arguments.Add("-loglevel");
        arguments.Add("error");
        arguments.Add("-threads");
        arguments.Add("2"); // Use multiple threads for decoding
        arguments.Add("-thread_queue_size");
        arguments.Add("1024"); // Increase thread queue size for smoother streaming
        
        // Input file specification
        arguments.Add("-i");
        arguments.Add(filePath);
        
        // Output format settings (these come after the input)
        arguments.Add("-ac");
        arguments.Add("2");
        arguments.Add("-f");
        arguments.Add("s16le");
        arguments.Add("-ar");
        arguments.Add("48000");
        
        // Apply volume filter if needed
        if (Math.Abs(volumeMultiplier - 1.0f) > 0.01f) // Only apply if significantly different from 1.0
        {
            arguments.Add("-af");
            arguments.Add($"volume={volumeMultiplier}");
            _logger.LogDebug("Applied volume filter: {Volume}x", volumeMultiplier);
        }
        
        // Buffering and streaming optimizations
        arguments.Add("-fflags");
        arguments.Add("+flush_packets"); // Flush packets immediately for real-time streaming
        arguments.Add("-flags");
        arguments.Add("low_delay"); // Minimize delay for real-time audio
        arguments.Add("-probesize");
        arguments.Add("32"); // Smaller probe size for faster startup
        arguments.Add("-analyzeduration");
        arguments.Add("0"); // Skip analysis for faster startup
        
        arguments.Add("pipe:1");

        _logger.LogInformation("FFMPEG command: ffmpeg {Args}", string.Join(" ", arguments));
        _logger.LogDebug("FFMPEG working directory: {Dir}", startInfo.WorkingDirectory);

        return startInfo;
    }

    private void SetProcessPriority(Process ffmpeg)
    {
        try
        {
            // Try different priority levels in order of preference
            // Start with High for best performance, fall back to lower levels if permission denied
            var prioritiesToTry = new[]
            {
                ProcessPriorityClass.High,
                ProcessPriorityClass.AboveNormal,
                ProcessPriorityClass.Normal
            };

            Exception? lastException = null;
            foreach (var priority in prioritiesToTry)
            {
                try
                {
                    ffmpeg.PriorityClass = priority;
                    _logger.LogInformation("Set FFMPEG process priority to {Priority}", priority);
                    return; // Success, exit early
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    _logger.LogDebug("Could not set FFMPEG priority to {Priority}: {Error}", priority, ex.Message);
                }
            }

            // If we get here, all priority attempts failed
            _logger.LogDebug("Unable to set FFMPEG process priority (common in containerized environments): {Error}", 
                lastException?.Message ?? "Unknown error");
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Process priority setting failed: {Error}", ex.Message);
        }
    }

    private async Task HandleFfmpegCompletion(Process ffmpeg, Task<string> stderrTask, string filePath)
    {
        var stderr = await stderrTask;
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            _logger.LogError("FFMPEG stderr for file {FilePath}: {Stderr}", filePath, stderr);
        }

        if (ffmpeg.ExitCode != 0)
        {
            _logger.LogWarning("FFMPEG exited with non-zero code {ExitCode} for file: {FilePath}", ffmpeg.ExitCode, filePath);
        }
        else
        {
            _logger.LogInformation("FFMPEG completed successfully for file: {FilePath}", filePath);
            // Fire the playback completed event when the song finishes naturally
            PlaybackCompleted?.Invoke();
        }
    }

    private void LogResourceUsage(string context)
    {
        try
        {
            var proc = Process.GetCurrentProcess();
            var cpuTime = proc.TotalProcessorTime;
            var memoryMB = proc.WorkingSet64 / (1024 * 1024);
            _logger.LogDebug("[Perf] {Context}: CPU Time={CpuTime}ms, Memory={MemoryMB}MB", context, cpuTime.TotalMilliseconds, memoryMB);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log resource usage");
        }
    }
}