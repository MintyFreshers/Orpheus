using Microsoft.Extensions.Logging;
using NetCord.Gateway;
using NetCord.Rest;
using Orpheus.Configuration;
using Orpheus.Services.Transcription;
using Orpheus.Services.VoiceClientController;
using Orpheus.Services.Queue;
using Orpheus.Services.Downloader.Youtube;
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
    private const int SilenceDetectionMs = 2000;
    private const int SilenceFrameThreshold = SilenceDetectionMs / FrameLengthMs;
    private const short SilenceThreshold = 500;

    private readonly ILogger<WakeWordResponseHandler> _logger;
    private readonly BotConfiguration _discordConfiguration;
    private readonly ITranscriptionService _transcriptionService;
    private readonly IVoiceClientController _voiceClientController;
    private readonly ISongQueueService _queueService;
    private readonly IQueuePlaybackService _queuePlaybackService;
    private readonly IYouTubeDownloader _downloader;
    private readonly ConcurrentDictionary<ulong, UserTranscriptionSession> _activeSessions = new();
    private readonly ConcurrentDictionary<ulong, Queue<byte[]>> _audioBuffers = new();
    private readonly ConcurrentDictionary<ulong, int> _silenceFrameCounts = new();
    private readonly IOpusDecoder _opusDecoder;

    public WakeWordResponseHandler(
        ILogger<WakeWordResponseHandler> logger,
        BotConfiguration discordConfiguration,
        ITranscriptionService transcriptionService,
        IVoiceClientController voiceClientController,
        ISongQueueService queueService,
        IQueuePlaybackService queuePlaybackService,
        IYouTubeDownloader downloader)
    {
        _logger = logger;
        _discordConfiguration = discordConfiguration;
        _transcriptionService = transcriptionService;
        _voiceClientController = voiceClientController;
        _queueService = queueService;
        _queuePlaybackService = queuePlaybackService;
        _downloader = downloader;
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
            _logger.LogInformation("Wake word detected from user {UserId}, starting immediate transcription", userId);

            await InitiateTranscriptionSessionWithBufferedAudioAsync(userId, client);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle wake word detection");
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
                    _ = Task.Run(async () => await CompleteTranscriptionSessionAsync(userId));
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

        await ScheduleSessionTimeoutAsync(userId);
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

    private Task ScheduleSessionTimeoutAsync(ulong userId)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(TranscriptionTimeoutMs);
            await CompleteTranscriptionSessionAsync(userId);
        });
        
        return Task.CompletedTask;
    }

    private async Task CompleteTranscriptionSessionAsync(ulong userId)
    {
        if (!_activeSessions.TryRemove(userId, out var session))
        {
            return;
        }

        try
        {
            _logger.LogInformation("Ending transcription session for user {UserId}", userId);
            
            _silenceFrameCounts.TryRemove(userId, out _);

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
        var response = await ProcessVoiceCommandAsync(transcription, session.UserId, session.Client);

        var channelId = _discordConfiguration.DefaultChannelId;
        await session.Client.Rest.SendMessageAsync(channelId, new MessageProperties().WithContent(response));
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
            
            return currentSilenceFrames >= SilenceFrameThreshold;
        }
        
        return false;
    }

    private async Task<string> ProcessVoiceCommandAsync(string transcription, ulong userId, GatewayClient client)
    {
        if (string.IsNullOrWhiteSpace(transcription))
        {
            _logger.LogWarning("Received empty transcription from user {UserId}", userId);
            return CreateUserMentionResponse(userId, "I didn't hear anything clearly.");
        }

        var normalizedCommand = transcription.ToLowerInvariant().Trim();
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
                return CreateUserMentionResponse(userId, "I couldn't determine which server you're in. Make sure you're in a voice channel.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get guild context for voice command");
            return CreateUserMentionResponse(userId, "I couldn't determine the server context for this command.");
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

    private async Task<string?> TryProcessAdvancedVoiceCommandAsync(string normalizedCommand, ulong userId, GatewayClient client, Guild guild)
    {
        if (string.IsNullOrWhiteSpace(normalizedCommand))
        {
            return null;
        }

        _logger.LogInformation("Checking for advanced voice command: '{Command}' from user {UserId}", normalizedCommand, userId);

        // Handle "leave" command
        if (normalizedCommand.Equals("leave") || normalizedCommand.Contains("leave voice") || normalizedCommand.Contains("disconnect"))
        {
            _logger.LogInformation("Recognized leave command from user {UserId}", userId);
            var result = await _voiceClientController.LeaveVoiceChannelAsync(guild, client);
            return CreateUserMentionResponse(userId, result);
        }

        // Handle "playtest" command  
        if (normalizedCommand.Equals("playtest") || normalizedCommand.Contains("play test"))
        {
            _logger.LogInformation("Recognized playtest command from user {UserId}", userId);
            const string testFilePath = "Resources/ExampleTrack.mp3";
            
            if (!File.Exists(testFilePath))
            {
                return CreateUserMentionResponse(userId, $"Test file not found: {testFilePath}");
            }
            
            var result = await _voiceClientController.PlayMp3Async(guild, client, userId, testFilePath);
            return CreateUserMentionResponse(userId, result);
        }

        // Handle "play <song>" command
        if (normalizedCommand.StartsWith("play ") && normalizedCommand.Length > 5)
        {
            var songQuery = normalizedCommand.Substring(5).Trim();
            _logger.LogInformation("Recognized play command from user {UserId} with query: {Query}", userId, songQuery);
            
            try
            {
                return await ProcessPlayCommandAsync(songQuery, userId, guild, client);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing play command for query: {Query}", songQuery);
                return CreateUserMentionResponse(userId, "Failed to add the song to the queue.");
            }
        }

        // Command not recognized as advanced, return null to allow basic processing
        return null;
    }

    private async Task<string> ProcessPlayCommandAsync(string query, ulong userId, Guild guild, GatewayClient client)
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
                return CreateUserMentionResponse(userId, $"❌ No results found for: **{query}**");
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

        return CreateUserMentionResponse(userId, message);
    }

    private string ProcessBasicVoiceCommand(string normalizedCommand, ulong userId)
    {
        _logger.LogInformation("Processing basic voice command: '{Command}' from user {UserId}", normalizedCommand, userId);

        // Handle "say" commands
        if (IsSayCommand(normalizedCommand))
        {
            var contentToSay = ExtractSayCommandContent(normalizedCommand);
            _logger.LogInformation("Recognized say command from user {UserId}: '{Content}'", userId, contentToSay);
            return CreateUserMentionResponse(userId, contentToSay);
        }
        
        // Handle other basic commands
        if (normalizedCommand.Contains("hello") || normalizedCommand.Contains("hi"))
        {
            _logger.LogInformation("Recognized greeting from user {UserId}", userId);
            return CreateUserMentionResponse(userId, "Hello there!");
        }
        
        if (normalizedCommand.Contains("ping"))
        {
            _logger.LogInformation("Recognized ping command from user {UserId}", userId);
            return CreateUserMentionResponse(userId, "Pong!");
        }

        _logger.LogInformation("Unrecognized basic command: '{Command}' from user {UserId}", normalizedCommand, userId);
        return CreateUserMentionResponse(userId, "I don't understand that command. Try saying 'play [song]', 'leave', 'playtest', 'say hello', 'hello', or 'ping'.");
    }

    private static bool IsSayCommand(string normalizedCommand)
    {
        return normalizedCommand.StartsWith("say ") && normalizedCommand.Length > 4;
    }

    private static string ExtractSayCommandContent(string normalizedCommand)
    {
        return normalizedCommand.Substring(4).Trim();
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
}

internal class UserTranscriptionSession
{
    public ulong UserId { get; set; }
    public DateTime StartTime { get; set; }
    public List<byte> AudioData { get; set; } = new();
    public GatewayClient Client { get; set; } = null!;
}
