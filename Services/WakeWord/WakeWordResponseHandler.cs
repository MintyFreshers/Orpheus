using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using NetCord.Gateway;
using NetCord.Rest;
using Orpheus.Configuration;
using Orpheus.Services.Transcription;
using Orpheus.Services.VoiceClientController;
using Orpheus.Services.Queue;
using Orpheus.Services.Downloader.Youtube;
using Orpheus.Services;
using System.Collections.Concurrent;
using System.Linq;
using Concentus;

namespace Orpheus.Services.WakeWord;

public class WakeWordResponseHandler
{
    private const int DiscordSampleRate = 48000;
    private const int TranscriptionTimeoutMs = 8000;
    private const int FrameLengthMs = 20;
    private const int DiscordFrameSize = DiscordSampleRate / 1000 * FrameLengthMs;
    private const int AudioBufferDurationMs = 1000;
    private const int MaxBufferedFrames = AudioBufferDurationMs / FrameLengthMs;
    private const int SilenceDetectionMs = 800; // Reduced from 1500ms to 800ms for faster response
    private const int SilenceFrameThreshold = SilenceDetectionMs / FrameLengthMs;
    private const short SilenceThreshold = 300; // Reduced from 400 to be more sensitive to speech endings

    private readonly ILogger<WakeWordResponseHandler> _logger;
    private readonly BotConfiguration _discordConfiguration;
    private readonly ITranscriptionService _transcriptionService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ISongQueueService _queueService;
    private readonly IQueuePlaybackService _queuePlaybackService;
    private readonly IYouTubeDownloader _downloader;
    private readonly IMessageUpdateService _messageUpdateService;
    private readonly ConcurrentDictionary<ulong, UserTranscriptionSession> _activeSessions = new();
    private readonly ConcurrentDictionary<ulong, Queue<byte[]>> _audioBuffers = new();
    private readonly ConcurrentDictionary<ulong, int> _silenceFrameCounts = new();
    private readonly ConcurrentDictionary<ulong, CancellationTokenSource> _sessionTimeoutCancellations = new();
    private readonly IOpusDecoder _opusDecoder;

    public WakeWordResponseHandler(
        ILogger<WakeWordResponseHandler> logger,
        BotConfiguration discordConfiguration,
        ITranscriptionService transcriptionService,
        IServiceProvider serviceProvider,
        ISongQueueService queueService,
        IQueuePlaybackService queuePlaybackService,
        IYouTubeDownloader downloader,
        IMessageUpdateService messageUpdateService)
    {
        _logger = logger;
        _discordConfiguration = discordConfiguration;
        _transcriptionService = transcriptionService;
        _serviceProvider = serviceProvider;
        _queueService = queueService;
        _queuePlaybackService = queuePlaybackService;
        _downloader = downloader;
        _messageUpdateService = messageUpdateService;
        _opusDecoder = OpusCodecFactory.CreateDecoder(DiscordSampleRate, 1);
    }

    public async Task HandleWakeWordDetectionAsync(ulong userId, GatewayClient? client)
    {
        if (client == null)
        {
            _logger.LogWarning("Cannot send wake word response: Gateway client is null");
            return;
        }

        try
        {
            _logger.LogInformation("Wake word detected from user {UserId}, playing acknowledgment and starting transcription", userId);

            // Play wake word acknowledgment sound and wait for it to complete
            await PlayWakeWordAcknowledgmentAsync(userId, client);

            // Now start transcription with proper timing
            await InitiateTranscriptionSessionWithBufferedAudioAsync(userId, client);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle wake word detection");
        }
    }

    private async Task PlayWakeWordAcknowledgmentAsync(ulong userId, GatewayClient client)
    {
        try
        {
            const string acknowledgmentPath = "Resources/wake_acknowledgment_very_loud.mp3";
            
            if (!File.Exists(acknowledgmentPath))
            {
                _logger.LogWarning("Wake word acknowledgment file not found: {Path}", acknowledgmentPath);
                return;
            }

            // Get guild context from the client cache to determine where to play the sound
            var guild = client.Cache.Guilds.Values.FirstOrDefault(g => 
                g.VoiceStates.ContainsKey(userId) && g.VoiceStates[userId].ChannelId.HasValue);
                
            if (guild == null)
            {
                _logger.LogDebug("Cannot play wake word acknowledgment: user {UserId} not in voice channel", userId);
                return;
            }

            _logger.LogDebug("Playing wake word acknowledgment sound for user {UserId} with ducking", userId);
            
            var voiceClientController = _serviceProvider.GetRequiredService<IVoiceClientController>();
            
            // Play the acknowledgment sound with ducking and WAIT for it to complete
            await voiceClientController.PlayDuckedOverlayMp3Async(guild, client, userId, acknowledgmentPath);
            
            _logger.LogInformation("Wake word acknowledgment sound completed for user {UserId}, ducking remains active for transcription", userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in wake word acknowledgment for user {UserId}", userId);
        }
    }

    public Task ProcessAudioForTranscription(byte[] opusFrame, ulong userId)
    {
        try
        {
            BufferAudioFrame(opusFrame, userId);

            if (_activeSessions.TryGetValue(userId, out var session))
            {
                var pcmAudioData = ConvertOpusFrameToPcmBytes(opusFrame);
                session.AudioData.AddRange(pcmAudioData);

                if (DetectSilenceInAudioFrame(pcmAudioData, userId))
                {
                    _logger.LogInformation("Silence detected for user {UserId}, completing transcription session", userId);
                    
                    // Cancel the timeout since we're completing due to silence
                    if (_sessionTimeoutCancellations.TryRemove(userId, out var cancellationSource))
                    {
                        cancellationSource.Cancel();
                        cancellationSource.Dispose();
                    }
                    
                    _ = Task.Run(async () => await CompleteTranscriptionSessionAsync(userId, "silence"));
                }
                else
                {
                    _silenceFrameCounts[userId] = 0;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing audio frame for transcription");
        }

        return Task.CompletedTask;
    }

    private async Task InitiateTranscriptionSessionWithBufferedAudioAsync(ulong userId, GatewayClient client)
    {
        var session = CreateNewTranscriptionSession(userId, client);
        ClearUserAudioBuffer(userId);
        
        _activeSessions[userId] = session;
        _silenceFrameCounts[userId] = 0;

        // Create cancellation token source for this session's timeout
        var timeoutCancellation = new CancellationTokenSource();
        _sessionTimeoutCancellations[userId] = timeoutCancellation;

        await ScheduleSessionTimeoutAsync(userId, timeoutCancellation.Token);
        _logger.LogInformation("Started fresh transcription session for user {UserId}", userId);
    }

    private void BufferAudioFrame(byte[] opusFrame, ulong userId)
    {
        if (!_audioBuffers.TryGetValue(userId, out var buffer))
        {
            buffer = new Queue<byte[]>();
            _audioBuffers[userId] = buffer;
        }

        buffer.Enqueue(opusFrame);

        while (buffer.Count > MaxBufferedFrames)
        {
            buffer.Dequeue();
        }
    }


    private static UserTranscriptionSession CreateNewTranscriptionSession(ulong userId, GatewayClient client)
    {
        return new UserTranscriptionSession
        {
            UserId = userId,
            StartTime = DateTime.UtcNow,
            AudioData = new List<byte>(),
            Client = client
        };
    }

    private Task ScheduleSessionTimeoutAsync(ulong userId, CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TranscriptionTimeoutMs, cancellationToken);
                _logger.LogInformation("Transcription session timeout reached for user {UserId}", userId);
                await CompleteTranscriptionSessionAsync(userId, "timeout");
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Transcription session timeout cancelled for user {UserId} (likely due to silence detection)", userId);
            }
        }, cancellationToken);
        
        return Task.CompletedTask;
    }

    private async Task CompleteTranscriptionSessionAsync(ulong userId, string reason = "unknown")
    {
        if (!_activeSessions.TryRemove(userId, out var session))
        {
            return;
        }

        try
        {
            _logger.LogInformation("Ending transcription session for user {UserId} (reason: {Reason})", userId, reason);
            
            _silenceFrameCounts.TryRemove(userId, out _);
            
            // Clean up cancellation token if it exists
            if (_sessionTimeoutCancellations.TryRemove(userId, out var cancellationSource))
            {
                cancellationSource.Cancel();
                cancellationSource.Dispose();
            }

            // Disable ducking now that transcription is complete
            try
            {
                var voiceClientController = _serviceProvider.GetRequiredService<IVoiceClientController>();
                voiceClientController.SetAudioDucking(false);
                _logger.LogDebug("Disabled audio ducking after transcription completion for user {UserId}", userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to disable audio ducking for user {UserId}", userId);
            }

            if (session.AudioData.Count > 0)
            {
                await ProcessCollectedAudioAsync(session);
            }
            else
            {
                await SendNoAudioResponseAsync(userId, session.Client);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ending transcription session for user {UserId}", userId);
        }
    }

    private async Task ProcessCollectedAudioAsync(UserTranscriptionSession session)
    {
        var audioBytes = session.AudioData.ToArray();
        var transcription = await _transcriptionService.TranscribeAudioAsync(audioBytes);

        if (!string.IsNullOrEmpty(transcription))
        {
            await ProcessSuccessfulTranscriptionAsync(session, transcription);
        }
        else
        {
            await SendNoTranscriptionResponseAsync(session);
        }
    }

    private async Task ProcessSuccessfulTranscriptionAsync(UserTranscriptionSession session, string transcription)
    {
        // Process all voice commands directly in WakeWordResponseHandler to avoid circular dependency
        var commandResult = await ProcessVoiceCommandAsync(transcription, session.UserId, session.Client);

        var channelId = _discordConfiguration.DefaultChannelId;
        var sentMessage = await session.Client.Rest.SendMessageAsync(channelId, new MessageProperties().WithContent(commandResult.Response));

        // If this was a play command that added a song, register the message for updates
        if (commandResult.SongId != null)
        {
            _logger.LogDebug("Registering voice command message {MessageId} for song updates: {SongId}", sentMessage.Id, commandResult.SongId);
            await _messageUpdateService.RegisterMessageForSongUpdatesAsync(sentMessage.Id, channelId, session.Client, commandResult.SongId, commandResult.Response);
        }
    }

    private async Task SendNoTranscriptionResponseAsync(UserTranscriptionSession session)
    {
        _logger.LogWarning("No transcription result for user {UserId}", session.UserId);
        var channelId = _discordConfiguration.DefaultChannelId;
        var noTranscriptionMessage = CreateNoTranscriptionMessage(session.UserId);
        await session.Client.Rest.SendMessageAsync(channelId, noTranscriptionMessage);
    }

    private async Task SendNoAudioResponseAsync(ulong userId, GatewayClient client)
    {
        _logger.LogWarning("No audio data collected for user {UserId}", userId);
        var channelId = _discordConfiguration.DefaultChannelId;
        var noAudioMessage = CreateNoTranscriptionMessage(userId);
        await client.Rest.SendMessageAsync(channelId, noAudioMessage);
    }

    private byte[] ConvertOpusFrameToPcmBytes(byte[] opusFrame)
    {
        var pcmSamples = DecodeOpusFrameToPcmSamples(opusFrame);
        return ConvertPcmSamplesToBytes(pcmSamples);
    }

    private short[] DecodeOpusFrameToPcmSamples(byte[] opusFrame)
    {
        int frameSize = DiscordFrameSize;
        short[] pcm = new short[frameSize];
        _opusDecoder.Decode(opusFrame, pcm, frameSize);
        return pcm;
    }

    private static byte[] ConvertPcmSamplesToBytes(short[] pcmSamples)
    {
        byte[] pcmBytes = new byte[pcmSamples.Length * 2];
        Buffer.BlockCopy(pcmSamples, 0, pcmBytes, 0, pcmBytes.Length);
        return pcmBytes;
    }

    private static MessageProperties CreateNoTranscriptionMessage(ulong userId)
    {
        return new MessageProperties().WithContent($"<@{userId}> I didn't hear anything clearly.");
    }

    private void ClearUserAudioBuffer(ulong userId)
    {
        if (_audioBuffers.TryGetValue(userId, out var buffer))
        {
            buffer.Clear();
        }
    }

    private bool DetectSilenceInAudioFrame(byte[] pcmAudioData, ulong userId)
    {
        var audioLevel = CalculateAudioLevel(pcmAudioData);
        
        if (audioLevel < SilenceThreshold)
        {
            var currentSilenceFrames = _silenceFrameCounts.GetOrAdd(userId, 0) + 1;
            _silenceFrameCounts[userId] = currentSilenceFrames;
            
            var silenceDurationMs = currentSilenceFrames * FrameLengthMs;
            _logger.LogDebug("Silence detected for user {UserId}: frame {CurrentFrame}/{ThresholdFrames} ({SilenceDurationMs}ms/{ThresholdMs}ms), audio level: {AudioLevel}",
                userId, currentSilenceFrames, SilenceFrameThreshold, silenceDurationMs, SilenceDetectionMs, audioLevel);
            
            if (currentSilenceFrames >= SilenceFrameThreshold)
            {
                _logger.LogInformation("Silence threshold reached for user {UserId} after {SilenceDurationMs}ms", userId, silenceDurationMs);
                return true;
            }
        }
        else
        {
            var previousFrames = _silenceFrameCounts.GetValueOrDefault(userId, 0);
            if (previousFrames > 0)
            {
                _logger.LogDebug("Audio activity detected for user {UserId}, resetting silence counter (was {PreviousFrames} frames), audio level: {AudioLevel}",
                    userId, previousFrames, audioLevel);
            }
            _silenceFrameCounts[userId] = 0;
        }
        
        return false;
    }

    private async Task<VoiceCommandResult> ProcessVoiceCommandAsync(string transcription, ulong userId, GatewayClient client)
    {
        if (string.IsNullOrWhiteSpace(transcription))
        {
            _logger.LogWarning("Received empty transcription from user {UserId}", userId);
            return new VoiceCommandResult(CreateUserMentionResponse(userId, "I didn't hear anything clearly."));
        }

        var normalizedCommand = NormalizeVoiceCommand(transcription);
        _logger.LogInformation("Processing voice command: '{Command}' from user {UserId}", normalizedCommand, userId);

        // Get guild context from the client cache
        Guild? guild = null;
        try
        {
            // Find the guild where the user is currently in a voice channel
            guild = client.Cache.Guilds.Values.FirstOrDefault(g => 
                g.VoiceStates.ContainsKey(userId) && g.VoiceStates[userId].ChannelId != null);
            
            if (guild == null)
            {
                _logger.LogWarning("Could not find guild with user {UserId} in voice channel", userId);
                return new VoiceCommandResult(CreateUserMentionResponse(userId, "I couldn't determine which server you're in. Make sure you're in a voice channel."));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get guild context for voice command");
            return new VoiceCommandResult(CreateUserMentionResponse(userId, "I couldn't determine the server context for this command."));
        }

        // First try advanced commands (play, leave, playtest)
        var advancedCommandResponse = await TryProcessAdvancedVoiceCommandAsync(normalizedCommand, userId, client, guild);
        if (advancedCommandResponse != null)
        {
            return advancedCommandResponse;
        }

        // Fall back to basic commands (say, hello, ping)
        return ProcessBasicVoiceCommand(normalizedCommand, userId);
    }

    private async Task<VoiceCommandResult?> TryProcessAdvancedVoiceCommandAsync(string normalizedCommand, ulong userId, GatewayClient client, Guild guild)
    {
        if (string.IsNullOrWhiteSpace(normalizedCommand))
        {
            return null;
        }

        _logger.LogInformation("Checking for advanced voice command: '{Command}' from user {UserId}", normalizedCommand, userId);

        // Handle "leave" command - more flexible matching
        if (IsLeaveCommand(normalizedCommand))
        {
            _logger.LogInformation("Recognized leave command from user {UserId}", userId);
            var voiceClientController = _serviceProvider.GetRequiredService<IVoiceClientController>();
            var result = await voiceClientController.LeaveVoiceChannelAsync(guild, client);
            return new VoiceCommandResult(CreateUserMentionResponse(userId, result));
        }

        // Handle "playtest" command - more flexible matching
        if (IsPlaytestCommand(normalizedCommand))
        {
            _logger.LogInformation("Recognized playtest command from user {UserId}", userId);
            const string testFilePath = "Resources/ExampleTrack.mp3";
            
            if (!File.Exists(testFilePath))
            {
                return new VoiceCommandResult(CreateUserMentionResponse(userId, $"Test file not found: {testFilePath}"));
            }
            
            var voiceClientController = _serviceProvider.GetRequiredService<IVoiceClientController>();
            var result = await voiceClientController.PlayMp3Async(guild, client, userId, testFilePath);
            return new VoiceCommandResult(CreateUserMentionResponse(userId, result));
        }

        // Handle "play <song>" command - more flexible matching
        var playQuery = ExtractPlayQuery(normalizedCommand);
        if (!string.IsNullOrEmpty(playQuery))
        {
            _logger.LogInformation("Recognized play command from user {UserId} with query: {Query}", userId, playQuery);
            
            try
            {
                return await ProcessPlayCommandAsync(playQuery, userId, guild, client);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing play command for query: {Query}", playQuery);
                return new VoiceCommandResult(CreateUserMentionResponse(userId, "Failed to add the song to the queue."));
            }
        }

        // Command not recognized as advanced, return null to allow basic processing
        return null;
    }

    private async Task<VoiceCommandResult> ProcessPlayCommandAsync(string query, ulong userId, Guild guild, GatewayClient client)
    {
        string? url;
        string placeholderTitle;

        // Check if the input is a URL or a search query
        if (IsUrl(query))
        {
            url = query;
            placeholderTitle = GetPlaceholderTitle(url);
            _logger.LogDebug("Voice play input detected as URL: {Url}", url);
        }
        else
        {
            // It's a search query
            _logger.LogDebug("Voice play input detected as search query: {Query}", query);
            
            // Search for the first result
            url = await _downloader.SearchAndGetFirstUrlAsync(query);
            if (url == null)
            {
                return new VoiceCommandResult(CreateUserMentionResponse(userId, $"❌ No results found for: **{query}**"));
            }
            
            _logger.LogInformation("Voice search found URL: {Url} for query: {Query}", url, query);
            placeholderTitle = $"Found: {query}"; // Will be updated with real title
        }

        // Check if queue was empty before adding
        var wasQueueEmpty = _queueService.IsEmpty && _queueService.CurrentSong == null;

        // Create queued song immediately with placeholder title
        var queuedSong = new QueuedSong(placeholderTitle, url, userId);
        _queueService.EnqueueSong(queuedSong);

        var queuePosition = _queueService.Count;
        var message = wasQueueEmpty
            ? $"✅ Added **{placeholderTitle}** to queue and starting playback!"
            : $"✅ Added **{placeholderTitle}** to queue (position {queuePosition})";

        // Auto-start queue processing if queue was empty (first song added)
        if (wasQueueEmpty || !_queuePlaybackService.IsProcessing)
        {
            await _queuePlaybackService.StartQueueProcessingAsync(guild, client, userId);
        }

        return new VoiceCommandResult(CreateUserMentionResponse(userId, message), queuedSong.Id);
    }

    private VoiceCommandResult ProcessBasicVoiceCommand(string normalizedCommand, ulong userId)
    {
        _logger.LogInformation("Processing basic voice command: '{Command}' from user {UserId}", normalizedCommand, userId);

        // Handle "say" commands - more flexible matching
        var sayContent = ExtractSayContent(normalizedCommand);
        if (!string.IsNullOrEmpty(sayContent))
        {
            _logger.LogInformation("Recognized say command from user {UserId}: '{Content}'", userId, sayContent);
            return new VoiceCommandResult(CreateUserMentionResponse(userId, sayContent));
        }
        
        // Handle greetings - more flexible matching
        if (IsGreetingCommand(normalizedCommand))
        {
            _logger.LogInformation("Recognized greeting from user {UserId}", userId);
            return new VoiceCommandResult(CreateUserMentionResponse(userId, "Hello there!"));
        }
        
        // Handle ping command - more flexible matching
        if (IsPingCommand(normalizedCommand))
        {
            _logger.LogInformation("Recognized ping command from user {UserId}", userId);
            return new VoiceCommandResult(CreateUserMentionResponse(userId, "Pong!"));
        }

        _logger.LogInformation("Unrecognized basic command: '{Command}' from user {UserId}", normalizedCommand, userId);
        return new VoiceCommandResult(CreateUserMentionResponse(userId, "I don't understand that command. Try saying 'play [song]', 'leave', 'playtest', 'say hello', 'hello', or 'ping'."));
    }

    private static bool IsUrl(string input)
    {
        return Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string GetPlaceholderTitle(string url)
    {
        // Return immediate placeholder based on URL type - no async calls to avoid timeout
        if (url.Contains("youtube.com") || url.Contains("youtu.be"))
        {
            return "YouTube Video"; // Will be updated by background service
        }
        return "Audio Track";
    }

    private static string CreateUserMentionResponse(ulong userId, string message)
    {
        return $"<@{userId}> {message}";
    }

    private static int CalculateAudioLevel(byte[] pcmAudioData)
    {
        if (pcmAudioData.Length < 2)
            return 0;

        long sum = 0;
        for (int i = 0; i < pcmAudioData.Length - 1; i += 2)
        {
            var sample = Math.Abs(BitConverter.ToInt16(pcmAudioData, i));
            sum += sample;
        }

        return (int)(sum / (pcmAudioData.Length / 2));
    }

    private static string NormalizeVoiceCommand(string transcription)
    {
        if (string.IsNullOrWhiteSpace(transcription))
            return string.Empty;

        // Convert to lowercase and remove common punctuation
        var normalized = transcription.ToLowerInvariant()
            .Replace(".", "")
            .Replace(",", "")
            .Replace("?", "")
            .Replace("!", "")
            .Replace(";", "")
            .Replace(":", "")
            .Trim();

        // Normalize multiple spaces to single spaces
        while (normalized.Contains("  "))
        {
            normalized = normalized.Replace("  ", " ");
        }

        return normalized;
    }

    private static bool IsLeaveCommand(string normalizedCommand)
    {
        // Check for various ways to say "leave"
        return normalizedCommand == "leave" ||
               normalizedCommand == "disconnect" ||
               normalizedCommand == "exit" ||
               normalizedCommand == "quit" ||
               normalizedCommand.Contains("leave voice") ||
               normalizedCommand.Contains("disconnect from voice") ||
               normalizedCommand.StartsWith("leave ");
    }

    private static bool IsPlaytestCommand(string normalizedCommand)
    {
        // Check for various ways to say "playtest"
        return normalizedCommand == "playtest" ||
               normalizedCommand == "play test" ||
               normalizedCommand == "test play" ||
               normalizedCommand.Contains("play test") ||
               normalizedCommand.Contains("test audio") ||
               normalizedCommand.Contains("test sound");
    }

    private static string? ExtractPlayQuery(string normalizedCommand)
    {
        // More flexible play command extraction
        if (normalizedCommand.StartsWith("play ") && normalizedCommand.Length > 5)
        {
            return normalizedCommand.Substring(5).Trim();
        }

        // Handle variations like "can you play", "please play", etc.
        var playIndex = normalizedCommand.IndexOf(" play ", StringComparison.Ordinal);
        if (playIndex >= 0)
        {
            var afterPlay = normalizedCommand.Substring(playIndex + 6).Trim();
            if (!string.IsNullOrEmpty(afterPlay))
            {
                return afterPlay;
            }
        }

        return null;
    }

    private static bool IsGreetingCommand(string normalizedCommand)
    {
        return normalizedCommand == "hello" ||
               normalizedCommand == "hi" ||
               normalizedCommand == "hey" ||
               normalizedCommand.Contains("hello") ||
               normalizedCommand.Contains("hi there") ||
               normalizedCommand.Contains("good morning") ||
               normalizedCommand.Contains("good afternoon") ||
               normalizedCommand.Contains("good evening");
    }

    private static bool IsPingCommand(string normalizedCommand)
    {
        return normalizedCommand == "ping" ||
               normalizedCommand.Contains("ping");
    }

    private static string? ExtractSayContent(string normalizedCommand)
    {
        // Check for "say" at the beginning
        if (normalizedCommand.StartsWith("say ") && normalizedCommand.Length > 4)
        {
            return normalizedCommand.Substring(4).Trim();
        }

        // Handle variations like "can you say", "please say", etc.
        var sayIndex = normalizedCommand.IndexOf(" say ", StringComparison.Ordinal);
        if (sayIndex >= 0)
        {
            var afterSay = normalizedCommand.Substring(sayIndex + 5).Trim();
            if (!string.IsNullOrEmpty(afterSay))
            {
                return afterSay;
            }
        }

        return null;
    }
}

internal record VoiceCommandResult(string Response, string? SongId = null);

internal class UserTranscriptionSession
{
    public ulong UserId { get; set; }
    public DateTime StartTime { get; set; }
    public List<byte> AudioData { get; set; } = new();
    public GatewayClient Client { get; set; } = null!;
}
