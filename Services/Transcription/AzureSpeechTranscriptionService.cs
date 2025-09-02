using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace Orpheus.Services.Transcription;

public class AzureSpeechTranscriptionService : ITranscriptionService, IDisposable
{
    private const int DiscordSampleRate = 48000;
    private const int AzureSampleRate = 16000; // Azure Speech prefers 16kHz
    private const bool EnableDebugAudioSaving = false; // Disabled by default for Azure

    private readonly ILogger<AzureSpeechTranscriptionService> _logger;
    private readonly IConfiguration _configuration;
    private readonly object _initializationLock = new();
    
    private SpeechConfig? _speechConfig;
    private bool _isInitialized;

    public bool IsInitialized => _isInitialized;

    public AzureSpeechTranscriptionService(ILogger<AzureSpeechTranscriptionService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
            return;

        try
        {
            _logger.LogInformation("Initializing Azure Speech transcription service...");

            await InitializeAzureSpeechComponents();

            _logger.LogInformation("Azure Speech transcription service initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Azure Speech transcription service");
            PerformCleanup();
            throw;
        }
    }

    public async Task<string?> TranscribeAudioAsync(byte[] audioData)
    {
        if (!IsServiceReady())
        {
            _logger.LogWarning("Transcription service not initialized");
            return null;
        }

        try
        {
            // Convert Discord audio format to Azure Speech compatible format
            var convertedAudio = ConvertDiscordAudioToAzureFormat(audioData);

            return await ProcessAudioWithAzureSpeechAsync(convertedAudio);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during transcription");
            return null;
        }
    }

    public void Cleanup()
    {
        PerformCleanup();
    }

    public void Dispose()
    {
        PerformCleanup();
        GC.SuppressFinalize(this);
    }

    private Task InitializeAzureSpeechComponents()
    {
        lock (_initializationLock)
        {
            // Get Azure Speech service configuration
            var subscriptionKey = _configuration["AzureSpeech:SubscriptionKey"] 
                ?? Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
            var region = _configuration["AzureSpeech:Region"] 
                ?? Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION") 
                ?? "eastus";

            if (string.IsNullOrWhiteSpace(subscriptionKey))
            {
                throw new InvalidOperationException(
                    "Azure Speech subscription key is missing. Set AzureSpeech:SubscriptionKey in configuration or AZURE_SPEECH_KEY environment variable.");
            }

            // Create speech configuration
            _speechConfig = SpeechConfig.FromSubscription(subscriptionKey, region);
            _speechConfig.SpeechRecognitionLanguage = "en-US";
            
            // Optimize for voice commands and low latency  
            _speechConfig.SetProperty(PropertyId.SpeechServiceConnection_InitialSilenceTimeoutMs, "3000");
            _speechConfig.SetProperty(PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs, "300"); // Reduced from 500ms for faster response
            _speechConfig.SetProperty(PropertyId.Speech_SegmentationSilenceTimeoutMs, "300"); // Reduced from 500ms for faster response

            _isInitialized = true;
        }

        return Task.CompletedTask;
    }

    private bool IsServiceReady()
    {
        return _isInitialized && _speechConfig != null;
    }

    private byte[] ConvertDiscordAudioToAzureFormat(byte[] audioData)
    {
        // Convert from Discord format (48kHz, 16-bit PCM) to Azure format (16kHz, 16-bit PCM)
        var sourceAudioSamples = ConvertBytesToInt16Samples(audioData);
        var resampledSamples = PerformSampleRateConversion(sourceAudioSamples);
        var convertedAudio = ConvertInt16SamplesToBytes(resampledSamples);

        LogResamplingDetails(sourceAudioSamples.Length, resampledSamples.Length);

        return convertedAudio;
    }

    private static short[] ConvertBytesToInt16Samples(byte[] audioData)
    {
        var samples = new short[audioData.Length / 2];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = BitConverter.ToInt16(audioData, i * 2);
        }
        return samples;
    }

    private static short[] PerformSampleRateConversion(short[] sourceSamples)
    {
        // Simple decimation from 48kHz to 16kHz (3:1 ratio)
        int decimationFactor = DiscordSampleRate / AzureSampleRate;
        var resampledSamples = new short[sourceSamples.Length / decimationFactor];

        for (int i = 0; i < resampledSamples.Length; i++)
        {
            resampledSamples[i] = sourceSamples[i * decimationFactor];
        }

        return resampledSamples;
    }

    private static byte[] ConvertInt16SamplesToBytes(short[] int16Samples)
    {
        byte[] bytes = new byte[int16Samples.Length * 2];
        Buffer.BlockCopy(int16Samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private void LogResamplingDetails(int sourceLength, int targetLength)
    {
        _logger.LogDebug("Resampled audio from {SourceSamples} samples at {SourceRate}Hz to {TargetSamples} samples at {TargetRate}Hz",
            sourceLength, DiscordSampleRate, targetLength, AzureSampleRate);
    }

    private async Task<string?> ProcessAudioWithAzureSpeechAsync(byte[] audioData)
    {
        try
        {
            // Create audio format for the converted data
            var audioFormat = AudioStreamFormat.GetWaveFormatPCM((uint)AzureSampleRate, 16, 1);
            var pushStream = AudioInputStream.CreatePushStream(audioFormat);
            var audioConfig = AudioConfig.FromStreamInput(pushStream);
            
            // Create a new recognizer for this recognition session
            using var recognizer = new SpeechRecognizer(_speechConfig!, audioConfig);
            
            // Set up recognition result handling - collect all speech segments and combine them
            var recognitionCompletionSource = new TaskCompletionSource<string?>();
            var recognizedSegments = new List<string>();

            recognizer.Recognized += (s, e) =>
            {
                if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrWhiteSpace(e.Result.Text))
                {
                    var transcription = e.Result.Text.Trim();
                    _logger.LogInformation("Azure Speech transcribed: {Text}", transcription);
                    
                    // Collect all speech segments instead of overwriting
                    recognizedSegments.Add(transcription);
                }
                else
                {
                    _logger.LogDebug("Azure Speech recognition result: {Reason}", e.Result.Reason);
                }
            };

            recognizer.Canceled += (s, e) =>
            {
                _logger.LogWarning("Azure Speech recognition cancelled: {Reason} - {Error}", e.Reason, e.ErrorDetails);
                // Combine all recognized segments when session is cancelled
                var combinedResult = string.Join(" ", recognizedSegments).Trim();
                recognitionCompletionSource.TrySetResult(string.IsNullOrWhiteSpace(combinedResult) ? null : combinedResult);
            };

            recognizer.SessionStopped += (s, e) =>
            {
                _logger.LogDebug("Azure Speech recognition session stopped");
                // Combine all recognized segments when session stops
                var combinedResult = string.Join(" ", recognizedSegments).Trim();
                recognitionCompletionSource.TrySetResult(string.IsNullOrWhiteSpace(combinedResult) ? null : combinedResult);
            };

            // Start recognition
            await recognizer.StartContinuousRecognitionAsync();

            // Push audio data through the stream
            pushStream.Write(audioData);
            pushStream.Close();

            // Wait for recognition to complete with timeout
            var timeoutCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var recognitionResult = await recognitionCompletionSource.Task.WaitAsync(timeoutCancellation.Token);

            // Stop recognition
            await recognizer.StopContinuousRecognitionAsync();

            return recognitionResult;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Azure Speech recognition timed out");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Azure Speech recognition");
            return null;
        }
    }

    private async Task SaveDebugAudioFileAsync(byte[] audioData, string filenamePrefix)
    {
        try
        {
            var debugFilePath = CreateDebugFilePath(filenamePrefix);
            await WriteDiscordAudioToWavFileAsync(debugFilePath, audioData);

            _logger.LogDebug("Saved debug audio: {FilePath} ({Size} bytes)", debugFilePath, audioData.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save debug audio with prefix {Prefix}", filenamePrefix);
        }
    }

    private async Task SaveConvertedDebugAudioAsync(byte[] convertedAudio, string filenamePrefix)
    {
        try
        {
            var debugFilePath = CreateDebugFilePath(filenamePrefix);
            await WriteConvertedAudioToWavFileAsync(debugFilePath, convertedAudio);

            _logger.LogDebug("Saved debug converted audio: {FilePath} ({Size} bytes)", debugFilePath, convertedAudio.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save debug converted audio with prefix {Prefix}", filenamePrefix);
        }
    }

    private static string CreateDebugFilePath(string filenamePrefix)
    {
        var debugDirectory = Path.Combine(Environment.CurrentDirectory, "DebugAudio");
        Directory.CreateDirectory(debugDirectory);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        var filename = $"{filenamePrefix}_{timestamp}.wav";

        return Path.Combine(debugDirectory, filename);
    }

    private static async Task WriteDiscordAudioToWavFileAsync(string filePath, byte[] audioData)
    {
        using var writer = new WaveFileWriter(filePath, new WaveFormat(DiscordSampleRate, 16, 1));
        await writer.WriteAsync(audioData, 0, audioData.Length);
    }

    private static async Task WriteConvertedAudioToWavFileAsync(string filePath, byte[] audioData)
    {
        using var writer = new WaveFileWriter(filePath, new WaveFormat(AzureSampleRate, 16, 1));
        await writer.WriteAsync(audioData, 0, audioData.Length);
    }

    private void PerformCleanup()
    {
        lock (_initializationLock)
        {
            _speechConfig = null; // SpeechConfig doesn't implement IDisposable
            _isInitialized = false;
            _logger.LogInformation("Azure Speech transcription service cleaned up");
        }
    }
}