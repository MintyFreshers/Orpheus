using Concentus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orpheus.Configuration;
using Pv;
using System.Collections.Concurrent;

namespace Orpheus.Services.WakeWord;

public class ImprovedPicovoiceWakeWordService : IWakeWordDetectionService, IDisposable
{
    private readonly ILogger<ImprovedPicovoiceWakeWordService> _logger;
    private readonly IConfiguration _configuration;
    private readonly WakeWordConfiguration _wakeWordConfig;
    private Porcupine? _porcupine;
    private bool _isInitialized;
    private readonly object _initLock = new();
    private readonly ConcurrentDictionary<ulong, long> _lastDetectionTimes = new();
    private readonly Dictionary<ulong, List<short>> _pcmBuffers = new(); // Buffer for each user
    private readonly IOpusDecoder _opusDecoder;
    private string[] _enabledWakeWords = Array.Empty<string>();
    private int _detectionCooldownMs = 3000;
    
    // Audio constants
    private const int DISCORD_SAMPLE_RATE = 48000;
    private const int PICOVOICE_SAMPLE_RATE = 16000;
    private const int FRAME_LENGTH_MS = 20;
    private const int DISCORD_FRAME_SIZE = DISCORD_SAMPLE_RATE / 1000 * FRAME_LENGTH_MS;
    private const int PICOVOICE_FRAME_SIZE = PICOVOICE_SAMPLE_RATE / 1000 * FRAME_LENGTH_MS;

    public bool IsInitialized => _isInitialized;

    public event Action<ulong>? WakeWordDetected;

    public ImprovedPicovoiceWakeWordService(ILogger<ImprovedPicovoiceWakeWordService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _wakeWordConfig = new WakeWordConfiguration(configuration);
        _opusDecoder = OpusCodecFactory.CreateDecoder(DISCORD_SAMPLE_RATE, 1); // 48kHz sample rate, 1 channel (mono)
    }

    public void Initialize()
    {
        lock (_initLock)
        {
            if (_isInitialized)
                return;

            try
            {
                var picovoiceAccessKey = _configuration["PicovoiceAccessKey"];
                if (string.IsNullOrEmpty(picovoiceAccessKey))
                {
                    _logger.LogError("Picovoice access key not found in configuration.");
                    return;
                }

                _enabledWakeWords = _wakeWordConfig.EnabledWakeWords;
                _detectionCooldownMs = _wakeWordConfig.DetectionCooldownMs;

                if (_enabledWakeWords.Length == 0)
                {
                    _logger.LogError("No wake words configured. Please set WakeWord:EnabledWords in configuration.");
                    return;
                }

                _logger.LogInformation("Initializing wake word detection with words: {WakeWords}", string.Join(", ", _enabledWakeWords));

                // Determine if we have built-in keywords, custom keywords, or a mix
                var builtInKeywords = new List<BuiltInKeyword>();
                var customKeywordPaths = new List<string>();
                var builtInSensitivities = new List<float>();
                var customSensitivities = new List<float>();

                foreach (var wakeWord in _enabledWakeWords)
                {
                    var sensitivity = _wakeWordConfig.GetSensitivity(wakeWord);
                    var builtInKeyword = _wakeWordConfig.TryParseBuiltInKeyword(wakeWord);
                    
                    if (builtInKeyword.HasValue)
                    {
                        builtInKeywords.Add(builtInKeyword.Value);
                        builtInSensitivities.Add(sensitivity);
                        _logger.LogDebug("Added built-in wake word: {WakeWord} with sensitivity {Sensitivity}", wakeWord, sensitivity);
                    }
                    else
                    {
                        var modelPath = _wakeWordConfig.GetCustomModelPath(wakeWord);
                        if (!File.Exists(modelPath))
                        {
                            _logger.LogWarning("Custom wake word model file not found: {Path} for word '{WakeWord}'. Skipping.", modelPath, wakeWord);
                            continue;
                        }
                        customKeywordPaths.Add(modelPath);
                        customSensitivities.Add(sensitivity);
                        _logger.LogDebug("Added custom wake word: {WakeWord} from {Path} with sensitivity {Sensitivity}", wakeWord, modelPath, sensitivity);
                    }
                }

                // Porcupine doesn't support mixing built-in and custom keywords in a single instance
                // We need to choose one approach. Prioritize built-in keywords if available.
                if (builtInKeywords.Count > 0)
                {
                    if (customKeywordPaths.Count > 0)
                    {
                        _logger.LogWarning("Both built-in and custom wake words configured. Using built-in keywords only: {BuiltIn}. Custom keywords will be ignored: {Custom}",
                            string.Join(", ", builtInKeywords), string.Join(", ", _enabledWakeWords.Where(w => !_wakeWordConfig.IsBuiltInKeyword(w))));
                    }
                    
                    // Built-in keywords only
                    _porcupine = Porcupine.FromBuiltInKeywords(
                        accessKey: picovoiceAccessKey,
                        keywords: builtInKeywords,
                        sensitivities: builtInSensitivities
                    );
                    
                    // Update enabled wake words to only include the built-in ones that were successfully added
                    _enabledWakeWords = _enabledWakeWords.Where(w => _wakeWordConfig.IsBuiltInKeyword(w)).ToArray();
                }
                else if (customKeywordPaths.Count > 0)
                {
                    // Custom keywords only
                    _porcupine = Porcupine.FromKeywordPaths(
                        accessKey: picovoiceAccessKey,
                        keywordPaths: customKeywordPaths,
                        sensitivities: customSensitivities
                    );
                    
                    // Update enabled wake words to only include the custom ones that were successfully added
                    var validCustomWords = new List<string>();
                    var originalCustomCount = 0;
                    foreach (var wakeWord in _enabledWakeWords)
                    {
                        if (!_wakeWordConfig.IsBuiltInKeyword(wakeWord))
                        {
                            originalCustomCount++;
                            var modelPath = _wakeWordConfig.GetCustomModelPath(wakeWord);
                            if (File.Exists(modelPath))
                            {
                                validCustomWords.Add(wakeWord);
                            }
                        }
                    }
                    _enabledWakeWords = validCustomWords.ToArray();
                }
                else
                {
                    _logger.LogError("No valid wake words found after filtering. Check your configuration and model files.");
                    return;
                }

                _isInitialized = true;
                _logger.LogInformation("Wake word detection initialized successfully with {Count} keywords: {Keywords}", 
                    _enabledWakeWords.Length, string.Join(", ", _enabledWakeWords));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize wake word detection");
                Cleanup();
            }
        }
    }

    public bool ProcessAudioFrame(byte[] opusFrame, ulong userId)
    {
        if (!_isInitialized || _porcupine == null)
            return false;

        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (_lastDetectionTimes.TryGetValue(userId, out var lastDetection) &&
                now - lastDetection < _detectionCooldownMs)
            {
                return false;
            }

            short[] pcmSamples = ConvertOpusFrameToPcm(opusFrame);
            int requiredFrameLength = _porcupine.FrameLength;

            // Buffer PCM samples for this user
            if (!_pcmBuffers.TryGetValue(userId, out var buffer))
            {
                buffer = new List<short>();
                _pcmBuffers[userId] = buffer;
            }
            buffer.AddRange(pcmSamples);

            bool detected = false;
            // Process as many full frames as possible
            while (buffer.Count >= requiredFrameLength)
            {
                short[] frame = buffer.GetRange(0, requiredFrameLength).ToArray();
                buffer.RemoveRange(0, requiredFrameLength);
                int keywordIndex = _porcupine.Process(frame);
                if (keywordIndex != -1)
                {
                    var detectedWakeWord = keywordIndex < _enabledWakeWords.Length 
                        ? _enabledWakeWords[keywordIndex] 
                        : $"unknown-{keywordIndex}";
                        
                    _logger.LogInformation("Wake word '{WakeWord}' detected from user {UserId}! (index: {Index})", 
                        detectedWakeWord, userId, keywordIndex);
                    
                    _lastDetectionTimes[userId] = now;
                    detected = true;
                    WakeWordDetected?.Invoke(userId);
                    break; // Only report one detection per call
                }
            }
            return detected;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing audio frame for wake word detection");
            return false;
        }
    }

    private short[] ConvertOpusFrameToPcm(byte[] opusFrame)
    {
        // Decode the Opus frame to raw PCM
        int frameSize = DISCORD_FRAME_SIZE;
        short[] pcm = new short[frameSize];
        _opusDecoder.Decode(opusFrame, pcm, frameSize);

        // Resample from Discord's 48kHz to Porcupine's required 16kHz
        short[] resampledPcm = new short[PICOVOICE_FRAME_SIZE];
        int resampleFactor = DISCORD_SAMPLE_RATE / PICOVOICE_SAMPLE_RATE;
        for (int i = 0; i < PICOVOICE_FRAME_SIZE; i++)
        {
            resampledPcm[i] = pcm[i * resampleFactor];
        }

        return resampledPcm;
    }

    public void Cleanup()
    {
        lock (_initLock)
        {
            if (_porcupine != null)
            {
                _porcupine.Dispose();
                _porcupine = null;
            }
            _isInitialized = false;
            _pcmBuffers.Clear();
        }
    }

    public void Dispose()
    {
        Cleanup();
        GC.SuppressFinalize(this);
    }
}