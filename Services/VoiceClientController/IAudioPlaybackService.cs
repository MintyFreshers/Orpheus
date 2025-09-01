using NetCord.Gateway.Voice;

namespace Orpheus.Services.VoiceClientController;

public interface IAudioPlaybackService
{
    Task PlayMp3ToStreamAsync(string filePath, OpusEncodeStream outputStream, CancellationToken cancellationToken = default);
    Task PlayOverlayMp3Async(string filePath, OpusEncodeStream outputStream, CancellationToken cancellationToken = default);
    Task StopPlaybackAsync();
    event Action? PlaybackCompleted;
}