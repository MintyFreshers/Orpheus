using Microsoft.Extensions.Logging;
using NetCord.Gateway;
using NetCord.Gateway.Voice;
using NetCord.Logging;
using Orpheus.Services.WakeWord;

namespace Orpheus.Services.VoiceClientController;

public class VoiceClientController : IVoiceClientController
{
    private readonly ILogger<VoiceClientController> _logger;
    private readonly IAudioPlaybackService _audioPlaybackService;
    private readonly IWakeWordDetectionService _wakeWordDetectionService;
    private readonly WakeWordResponseHandler _wakeWordResponseHandler;
    private readonly string _instanceGuid;
    private VoiceClient? _voiceClient;
    private GatewayClient? _lastGatewayClient;

    public VoiceClientController(
        ILogger<VoiceClientController> logger,
        IAudioPlaybackService audioPlaybackService,
        IWakeWordDetectionService wakeWordDetectionService,
        WakeWordResponseHandler wakeWordResponseHandler)
    {
        _logger = logger;
        _audioPlaybackService = audioPlaybackService;
        _wakeWordDetectionService = wakeWordDetectionService;
        _wakeWordResponseHandler = wakeWordResponseHandler;
        _instanceGuid = Guid.NewGuid().ToString();

        _logger.LogInformation("VoiceClientController instance created with GUID: {Guid}", _instanceGuid);

        SubscribeToWakeWordEvents();
    }

    public async Task<string> JoinVoiceChannelOfUserAsync(Guild guild, GatewayClient client, ulong userId)
    {
        _lastGatewayClient = client;

        if (IsBotInVoiceChannel(guild, client.Id))
        {
            return "I'm already connected to a voice channel!";
        }

        var userVoiceState = GetUserVoiceState(guild, userId);
        if (userVoiceState == null)
        {
            return "You are not connected to any voice channel!";
        }

        try
        {
            await JoinVoiceChannelAsync(guild, userVoiceState, client);
            await InitializeVoiceClientAsync();
            _ = StartWakeWordListening();

            return "Joined voice channel.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to join voice channel");
            return $"Failed to join voice channel: {ex.Message}";
        }
    }

    public async Task<string> LeaveVoiceChannelAsync(Guild guild, GatewayClient client)
    {
        if (!IsBotInVoiceChannel(guild, client.Id))
        {
            return "I'm not connected to any voice channel!";
        }

        try
        {
            await DisconnectBotFromVoiceChannel(guild, client);
            DisposeVoiceClient();

            return "Left the voice channel.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to leave voice channel");
            return $"Failed to leave voice channel: {ex.Message}";
        }
    }

    public async Task<string> StartEchoingAsync(Guild guild, GatewayClient client, ulong userId)
    {
        if (!IsBotInVoiceChannel(guild, client.Id) || _voiceClient == null)
        {
            var joinResult = await JoinVoiceChannelOfUserAsync(guild, client, userId);
            if (_voiceClient == null)
            {
                return joinResult;
            }
        }

        try
        {
            var outputStream = _voiceClient!.CreateOutputStream(normalizeSpeed: false);
            _voiceClient.VoiceReceive += args =>
            {
                if (args.UserId == userId)
                {
                    return outputStream.WriteAsync(args.Frame);
                }
                return default;
            };

            return "Echoing your voice!";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start echoing");
            return $"Failed to start echoing: {ex.Message}";
        }
    }

    public async Task<string> PlayMp3Async(Guild guild, GatewayClient client, ulong userId, string filePath)
    {
        _logger.LogDebug("PlayMp3Async called with guild: {GuildId}, user: {UserId}, file: {FilePath}", guild.Id, userId, filePath);
        
        if (!IsBotInVoiceChannel(guild, client.Id) || _voiceClient == null)
        {
            _logger.LogDebug("Bot not in voice channel, attempting to join...");
            try
            {
                var joinResult = await JoinVoiceChannelOfUserAsync(guild, client, userId);
                _logger.LogDebug("Join attempt completed with result: {Result}", joinResult);
                if (_voiceClient == null)
                {
                    _logger.LogWarning("Voice client was not initialized after join attempt. Join result: {Result}", joinResult);
                    return joinResult;
                }
                _logger.LogDebug("Successfully joined voice channel for playback");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while joining voice channel");
                return $"Failed to join voice channel: {ex.Message}";
            }
        }
        else
        {
            _logger.LogDebug("Bot is already in voice channel, proceeding with playback");
        }

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Requested MP3 file not found: {FilePath}", filePath);
            return $"File not found: {filePath}";
        }

        _logger.LogDebug("File exists, checking file size...");
        var fileInfo = new FileInfo(filePath);
        _logger.LogDebug("File size: {FileSize} bytes", fileInfo.Length);

        if (fileInfo.Length == 0)
        {
            _logger.LogWarning("MP3 file is empty: {FilePath}", filePath);
            return $"File is empty: {filePath}";
        }

        try
        {
            _logger.LogDebug("Entering speaking state...");
            await _voiceClient!.EnterSpeakingStateAsync(SpeakingFlags.Microphone);
            
            _logger.LogDebug("Creating opus output stream...");
            var outputStream = CreateOpusOutputStream();
            _logger.LogDebug("Opus output stream created successfully");
            
            _logger.LogDebug("Starting audio playback task...");
            // Start playback as a fire-and-forget task but ensure proper prioritization
            _ = Task.Run(async () =>
            {
                try
                {
                    // Set high priority for the playback coordination thread
                    Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
                    _logger.LogDebug("Audio playback task started with high priority");
                    await _audioPlaybackService.PlayMp3ToStreamAsync(filePath, outputStream);
                    _logger.LogDebug("Audio playback task completed successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Audio playback task failed for file: {FilePath}", filePath);
                }
            }, CancellationToken.None);

            _logger.LogInformation("Started playback of file: {FilePath}", filePath);
            return "Playing MP3 file!";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to play MP3 file: {FilePath}", filePath);
            return $"Failed to play MP3: {ex.Message}";
        }
    }

    public async Task<string> PlayOverlayMp3Async(Guild guild, GatewayClient client, ulong userId, string filePath)
    {
        _logger.LogDebug("PlayOverlayMp3Async called with guild: {GuildId}, user: {UserId}, file: {FilePath}", guild.Id, userId, filePath);
        
        if (!IsBotInVoiceChannel(guild, client.Id) || _voiceClient == null)
        {
            _logger.LogDebug("Bot not in voice channel for overlay, skipping overlay sound");
            return "Bot not in voice channel - overlay sound skipped";
        }

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Requested overlay MP3 file not found: {FilePath}", filePath);
            return $"Overlay file not found: {filePath}";
        }

        _logger.LogDebug("File exists for overlay, checking file size...");
        var fileInfo = new FileInfo(filePath);
        _logger.LogDebug("Overlay file size: {FileSize} bytes", fileInfo.Length);

        if (fileInfo.Length == 0)
        {
            _logger.LogWarning("Overlay MP3 file is empty: {FilePath}", filePath);
            return $"Overlay file is empty: {filePath}";
        }

        try
        {
            _logger.LogDebug("Creating opus output stream for overlay...");
            var outputStream = CreateOpusOutputStream();
            _logger.LogDebug("Opus output stream created successfully for overlay");
            
            _logger.LogDebug("Starting overlay audio playback task...");
            // Start overlay playback as a fire-and-forget task with high priority
            _ = Task.Run(async () =>
            {
                try
                {
                    // Set highest priority for overlay sounds to ensure they play immediately
                    Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
                    _logger.LogDebug("Overlay audio playbook task started with AboveNormal priority");
                    await _audioPlaybackService.PlayOverlayMp3Async(filePath, outputStream);
                    _logger.LogDebug("Overlay audio playback task completed successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Overlay audio playback task failed for file: {FilePath}", filePath);
                }
            }, CancellationToken.None);

            _logger.LogInformation("Started overlay playback of file: {FilePath}", filePath);
            return "Playing overlay MP3 file!";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to play overlay MP3 file: {FilePath}", filePath);
            return $"Failed to play overlay MP3: {ex.Message}";
        }
    }

    public void SetAudioDucking(bool enabled)
    {
        _audioPlaybackService.SetDucking(enabled);
        _logger.LogDebug("Audio ducking set to {Enabled} via VoiceClientController", enabled);
    }

    public async Task<string> StopPlaybackAsync()
    {
        try
        {
            await _audioPlaybackService.StopPlaybackAsync();
            _logger.LogInformation("Playback stopped via universal stop command");
            return "Playback stopped.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop playback");
            return $"Failed to stop playback: {ex.Message}";
        }
    }

    private void SubscribeToWakeWordEvents()
    {
        _wakeWordDetectionService.WakeWordDetected += async (wakeUserId) =>
        {
            await _wakeWordResponseHandler.HandleWakeWordDetectionAsync(wakeUserId, _lastGatewayClient);
        };
    }

    private async Task JoinVoiceChannelAsync(Guild guild, VoiceState userVoiceState, GatewayClient client)
    {
        _voiceClient = await client.JoinVoiceChannelAsync(
            guild.Id,
            userVoiceState.ChannelId.GetValueOrDefault(),
            new VoiceClientConfiguration
            {
                RedirectInputStreams = true,
                Logger = new ConsoleLogger(),
            });
    }

    private async Task InitializeVoiceClientAsync()
    {
        if (_voiceClient == null)
        {
            throw new InvalidOperationException("Voice client is not initialized");
        }

        await _voiceClient.StartAsync();
        await _voiceClient.EnterSpeakingStateAsync(SpeakingFlags.Microphone);
    }

    private Task StartWakeWordListening()
    {
        if (_voiceClient == null)
        {
            return Task.CompletedTask;
        }

        _wakeWordDetectionService.Initialize();
        _voiceClient.VoiceReceive += args =>
        {
            if (args.UserId != 0)
            {
                var frameData = args.Frame.ToArray();
                // Process for wake word detection
                _wakeWordDetectionService.ProcessAudioFrame(frameData, args.UserId);
                // Also process for transcription if there's an active session
                _ = _wakeWordResponseHandler.ProcessAudioForTranscription(frameData, args.UserId);
            }
            return default;
        };

        return Task.CompletedTask;
    }

    private OpusEncodeStream CreateOpusOutputStream()
    {
        var outStream = _voiceClient!.CreateOutputStream();
        // Use Audio application mode for better music quality and enable DTX for better performance
        return new OpusEncodeStream(outStream, PcmFormat.Short, VoiceChannels.Stereo, OpusApplication.Audio);
    }

    private bool IsBotInVoiceChannel(Guild guild, ulong botId)
    {
        return guild.VoiceStates.TryGetValue(botId, out var botVoiceState) && botVoiceState.ChannelId is not null;
    }

    private VoiceState? GetUserVoiceState(Guild guild, ulong userId)
    {
        if (!guild.VoiceStates.TryGetValue(userId, out var voiceState) || voiceState.ChannelId is null)
        {
            return null;
        }
        return voiceState;
    }

    private async Task DisconnectBotFromVoiceChannel(Guild guild, GatewayClient client)
    {
        var emptyChannelVoiceStateProperties = new VoiceStateProperties(guild.Id, null);
        await client.UpdateVoiceStateAsync(emptyChannelVoiceStateProperties);
    }

    private void DisposeVoiceClient()
    {
        if (_voiceClient != null)
        {
            _voiceClient.Dispose();
            _voiceClient = null;
        }
    }
}