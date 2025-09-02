using NetCord.Gateway.Voice;

namespace Orpheus.Services.VoiceClientController;

public interface IAudioPlaybackService
{
    Task PlayMp3ToStreamAsync(string filePath, OpusEncodeStream outputStream, CancellationToken cancellationToken = default);
    Task PlayOverlayMp3Async(string filePath, OpusEncodeStream outputStream, CancellationToken cancellationToken = default);
    Task PlayOverlayMp3Async(string filePath, OpusEncodeStream outputStream, float volumeMultiplier, CancellationToken cancellationToken = default);
    Task PlayDuckedOverlayMp3Async(string filePath, OpusEncodeStream outputStream, CancellationToken cancellationToken = default);
    Task PlayDuckedOverlayMp3Async(string filePath, OpusEncodeStream outputStream, float volumeMultiplier, CancellationToken cancellationToken = default);
    Task StopPlaybackAsync();
    void SetDucking(bool enabled);
    event Action? PlaybackCompleted;
    event Action<bool>? OnDuckingChanged;
}