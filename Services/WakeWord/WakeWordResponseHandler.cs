using Microsoft.Extensions.Logging;
using NetCord.Gateway;
using NetCord.Rest;
using Orpheus.Configuration;
using Orpheus.Services.Transcription;
using System.Collections.Concurrent;
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
    private readonly IVoiceCommandProcessor _voiceCommandProcessor;
    private readonly ConcurrentDictionary<ulong, UserTranscriptionSession> _activeSessions = new();
    private readonly ConcurrentDictionary<ulong, Queue<byte[]>> _audioBuffers = new();
    private readonly ConcurrentDictionary<ulong, int> _silenceFrameCounts = new();
    private readonly IOpusDecoder _opusDecoder;

    public WakeWordResponseHandler(
        ILogger<WakeWordResponseHandler> logger,
        BotConfiguration discordConfiguration,
        ITranscriptionService transcriptionService,
        IVoiceCommandProcessor voiceCommandProcessor)
    {
        _logger = logger;
        _discordConfiguration = discordConfiguration;
        _transcriptionService = transcriptionService;
        _voiceCommandProcessor = voiceCommandProcessor;
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
        var response = await _voiceCommandProcessor.ProcessCommandAsync(transcription, session.UserId, session.Client);
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
