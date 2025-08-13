using Microsoft.Extensions.Logging;
using Whisper.net;
using Whisper.net.Ggml;
using NAudio.Wave;

namespace Orpheus.Services.Transcription;

public class WhisperTranscriptionService : ITranscriptionService, IDisposable
{
    private const string ModelFileName = "ggml-small.bin";
    private const bool EnableDebugAudioSaving = true;
    private const string LanguageCode = "en";
    private const int DiscordSampleRate = 48000;
    private const int WhisperSampleRate = 16000;
    private const float NormalizationFactor = 32768.0f;

    private readonly ILogger<WhisperTranscriptionService> _logger;
    private readonly object _initializationLock = new();
    
    private WhisperFactory? _whisperFactory;
    private WhisperProcessor? _whisperProcessor;
    private bool _isInitialized;

    public bool IsInitialized => _isInitialized;

    public WhisperTranscriptionService(ILogger<WhisperTranscriptionService> logger)
    {
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
            return;

        try
        {
            _logger.LogInformation("Initializing Whisper transcription service...");

            var modelPath = await EnsureModelExistsAsync();
            await InitializeWhisperComponents(modelPath);

            _logger.LogInformation("Whisper transcription service initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Whisper transcription service");
            PerformCleanup();
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
            if (EnableDebugAudioSaving)
            {
                await SaveDebugAudioFileAsync(audioData, "raw_input");
            }

            var resampledAudio = ConvertDiscordAudioToWhisperFormat(audioData);

            if (EnableDebugAudioSaving)
            {
                await SaveResampledDebugAudioAsync(resampledAudio, "resampled_16khz");
            }

            return await ProcessAudioWithWhisperAsync(resampledAudio);
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

    private async Task<string> EnsureModelExistsAsync()
    {
        var modelPath = GetModelPath();
        CreateModelDirectoryIfNeeded(modelPath);

        if (!File.Exists(modelPath))
        {
            await DownloadWhisperModelAsync(modelPath);
        }

        return modelPath;
    }

    private static string GetModelPath()
    {
        return Path.Combine(Environment.CurrentDirectory, "Models", ModelFileName);
    }

    private static void CreateModelDirectoryIfNeeded(string modelPath)
    {
        var modelDirectory = Path.GetDirectoryName(modelPath);
        if (modelDirectory != null)
        {
            Directory.CreateDirectory(modelDirectory);
        }
    }

    private async Task DownloadWhisperModelAsync(string modelPath)
    {
        _logger.LogInformation("Downloading Whisper small model for better transcription accuracy...");

        using var httpClient = new HttpClient();
        var downloader = new WhisperGgmlDownloader(httpClient);
        using var modelStream = await downloader.GetGgmlModelAsync(GgmlType.Small);
        using var fileWriter = File.Create(modelPath);
        await modelStream.CopyToAsync(fileWriter);

        _logger.LogInformation("Whisper small model downloaded successfully");
    }

    private Task InitializeWhisperComponents(string modelPath)
    {
        lock (_initializationLock)
        {
            _whisperFactory = WhisperFactory.FromPath(modelPath);
            _whisperProcessor = _whisperFactory.CreateBuilder()
                .WithLanguage(LanguageCode)
                .WithPrompt("This is a voice command for a Discord music bot. Common commands include: play, stop, pause, skip, queue, leave, hello, say")
                .WithProbabilities()
                .WithTemperature(0.1f)  // Lower temperature for more focused/consistent results
                .Build();

            _isInitialized = true;
        }

        return Task.CompletedTask;
    }

    private bool IsServiceReady()
    {
        return _isInitialized && _whisperProcessor != null;
    }

    private float[] ConvertDiscordAudioToWhisperFormat(byte[] audioData)
    {
        var sourceAudioSamples = ConvertBytesToInt16Samples(audioData);
        var normalizedFloatSamples = NormalizeInt16SamplesToFloat(sourceAudioSamples);
        var resampledAudio = PerformSampleRateConversion(normalizedFloatSamples);

        LogResamplingDetails(normalizedFloatSamples.Length, resampledAudio.Length);

        return resampledAudio;
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

    private static float[] NormalizeInt16SamplesToFloat(short[] sourceSamples)
    {
        var floatSamples = new float[sourceSamples.Length];
        for (int i = 0; i < sourceSamples.Length; i++)
        {
            floatSamples[i] = sourceSamples[i] / NormalizationFactor;
        }
        return floatSamples;
    }

    private static float[] PerformSampleRateConversion(float[] sourceFloats)
    {
        int decimationFactor = DiscordSampleRate / WhisperSampleRate;
        var resampledSamples = new float[sourceFloats.Length / decimationFactor];

        for (int i = 0; i < resampledSamples.Length; i++)
        {
            resampledSamples[i] = sourceFloats[i * decimationFactor];
        }

        return resampledSamples;
    }

    private void LogResamplingDetails(int sourceLength, int targetLength)
    {
        _logger.LogDebug("Resampled audio from {SourceSamples} samples at {SourceRate}Hz to {TargetSamples} samples at {TargetRate}Hz",
            sourceLength, DiscordSampleRate, targetLength, WhisperSampleRate);
    }

    private async Task<string?> ProcessAudioWithWhisperAsync(float[] audioSamples)
    {
        await foreach (var segment in _whisperProcessor!.ProcessAsync(audioSamples))
        {
            if (!string.IsNullOrWhiteSpace(segment.Text))
            {
                var transcription = segment.Text.Trim();
                _logger.LogInformation("Transcribed: {Text}", transcription);
                return transcription;
            }
        }

        return null;
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

    private async Task SaveResampledDebugAudioAsync(float[] samples, string filenamePrefix)
    {
        try
        {
            var debugFilePath = CreateDebugFilePath(filenamePrefix);
            await WriteResampledAudioToWavFileAsync(debugFilePath, samples);

            _logger.LogDebug("Saved debug resampled audio: {FilePath} ({Samples} samples)", debugFilePath, samples.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save debug resampled audio with prefix {Prefix}", filenamePrefix);
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

    private static async Task WriteResampledAudioToWavFileAsync(string filePath, float[] samples)
    {
        var int16Samples = ConvertFloatSamplesToInt16(samples);
        var audioBytes = ConvertInt16SamplesToBytes(int16Samples);

        using var writer = new WaveFileWriter(filePath, new WaveFormat(WhisperSampleRate, 16, 1));
        await writer.WriteAsync(audioBytes, 0, audioBytes.Length);
    }

    private static short[] ConvertFloatSamplesToInt16(float[] samples)
    {
        var int16Samples = new short[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            var clampedValue = Math.Max(-1.0f, Math.Min(1.0f, samples[i]));
            int16Samples[i] = (short)(clampedValue * 32767);
        }
        return int16Samples;
    }

    private static byte[] ConvertInt16SamplesToBytes(short[] int16Samples)
    {
        byte[] bytes = new byte[int16Samples.Length * 2];
        Buffer.BlockCopy(int16Samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private void PerformCleanup()
    {
        lock (_initializationLock)
        {
            _whisperProcessor?.Dispose();
            _whisperProcessor = null;
            _whisperFactory?.Dispose();
            _whisperFactory = null;
            _isInitialized = false;
            _logger.LogInformation("Whisper transcription service cleaned up");
        }
    }
}