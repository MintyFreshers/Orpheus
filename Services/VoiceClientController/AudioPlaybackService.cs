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

    public event Action? PlaybackCompleted;

    public AudioPlaybackService(ILogger<AudioPlaybackService> logger)
    {
        _logger = logger;
    }

    public async Task PlayMp3ToStreamAsync(string filePath, OpusEncodeStream outputStream, CancellationToken cancellationToken = default)
    {
        await StopPlaybackAsync();
        await Task.Delay(100, cancellationToken);

        LogResourceUsage("Before FFMPEG start");

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
                await ffmpeg.StandardOutput.BaseStream.CopyToAsync(outputStream, bufferSize, cancellationToken);
                await outputStream.FlushAsync(cancellationToken);

                _logger.LogInformation("Finished streaming audio for file: {FilePath}", filePath);

                await ffmpeg.WaitForExitAsync(cancellationToken);

                await HandleFfmpegCompletion(ffmpeg, stderrTask, filePath);
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

    public void SetDucking(bool enabled)
    {
        lock (_lock)
        {
            if (_isDucked == enabled)
                return;
                
            _isDucked = enabled;
            _logger.LogInformation("Audio ducking {Status} - new songs will play at {Volume}% volume", 
                enabled ? "enabled" : "disabled",
                enabled ? (int)(DuckedVolumeMultiplier * 100) : 100);
                
            // Note: Currently playing audio streams cannot have their volume adjusted at runtime
            // due to Discord voice client limitations. Only new streams will use the ducked volume.
            if (enabled && _currentFfmpegProcess != null && !_currentFfmpegProcess.HasExited)
            {
                _logger.LogDebug("Audio ducking enabled while audio is currently playing. " +
                    "Current stream will continue at normal volume. Ducking will apply to new streams.");
            }
        }
    }

    public Task StopPlaybackAsync()
    {
        lock (_lock)
        {
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