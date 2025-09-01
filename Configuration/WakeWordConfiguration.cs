using Microsoft.Extensions.Configuration;
using Pv;

namespace Orpheus.Configuration;

public class WakeWordConfiguration
{
    private readonly IConfiguration _configuration;

    public WakeWordConfiguration(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Gets the list of enabled wake words. Defaults to "computer" and "orpheus" if not configured.
    /// </summary>
    public string[] EnabledWakeWords => GetWakeWords("WakeWord:EnabledWords", new[] { "computer", "orpheus" });

    /// <summary>
    /// Gets the sensitivity for a specific wake word. Defaults to 0.7 if not configured.
    /// </summary>
    public float GetSensitivity(string wakeWord)
    {
        var configKey = $"WakeWord:Sensitivities:{wakeWord}";
        var configValue = _configuration[configKey];
        
        if (string.IsNullOrEmpty(configValue))
        {
            // Default sensitivities for different wake words
            return wakeWord.ToLowerInvariant() switch
            {
                "computer" => 0.6f,    // Built-in keyword, can be more sensitive
                "orpheus" => 0.7f,     // Custom keyword, slightly less sensitive
                "jarvis" => 0.6f,      // Built-in keyword
                "bumblebee" => 0.6f,   // Built-in keyword
                _ => 0.7f              // Default for any other wake word
            };
        }

        if (float.TryParse(configValue, out var parsedValue))
        {
            // Clamp sensitivity between 0.1 and 1.0
            return Math.Max(0.1f, Math.Min(1.0f, parsedValue));
        }

        return 0.7f; // Safe default
    }

    /// <summary>
    /// Gets the detection cooldown in milliseconds. Defaults to 3000ms if not configured.
    /// </summary>
    public int DetectionCooldownMs => GetIntValue("WakeWord:DetectionCooldownMs", 3000);

    /// <summary>
    /// Determines if the wake word is a built-in Porcupine keyword or requires a custom .ppn file.
    /// </summary>
    public bool IsBuiltInKeyword(string wakeWord)
    {
        return TryParseBuiltInKeyword(wakeWord) != null;
    }

    /// <summary>
    /// Tries to parse a wake word string to a BuiltInKeyword enum value.
    /// </summary>
    public BuiltInKeyword? TryParseBuiltInKeyword(string wakeWord)
    {
        var normalizedWord = wakeWord.ToUpperInvariant().Replace(" ", "_");
        
        return normalizedWord switch
        {
            "ALEXA" => BuiltInKeyword.ALEXA,
            "AMERICANO" => BuiltInKeyword.AMERICANO,
            "BLUEBERRY" => BuiltInKeyword.BLUEBERRY,
            "BUMBLEBEE" => BuiltInKeyword.BUMBLEBEE,
            "COMPUTER" => BuiltInKeyword.COMPUTER,
            "GRAPEFRUIT" => BuiltInKeyword.GRAPEFRUIT,
            "GRASSHOPPER" => BuiltInKeyword.GRASSHOPPER,
            "HEY_GOOGLE" or "HEY GOOGLE" => BuiltInKeyword.HEY_GOOGLE,
            "HEY_SIRI" or "HEY SIRI" => BuiltInKeyword.HEY_SIRI,
            "JARVIS" => BuiltInKeyword.JARVIS,
            "OK_GOOGLE" or "OK GOOGLE" => BuiltInKeyword.OK_GOOGLE,
            "PICOVOICE" => BuiltInKeyword.PICOVOICE,
            "PORCUPINE" => BuiltInKeyword.PORCUPINE,
            "TERMINATOR" => BuiltInKeyword.TERMINATOR,
            _ => null
        };
    }

    /// <summary>
    /// Gets the path to a custom wake word model file for non-built-in keywords.
    /// </summary>
    public string GetCustomModelPath(string wakeWord)
    {
        var configKey = $"WakeWord:CustomModels:{wakeWord}";
        var configValue = _configuration[configKey];
        
        if (!string.IsNullOrEmpty(configValue))
        {
            return configValue;
        }
        
        // Default path for orpheus
        if (string.Equals(wakeWord, "orpheus", StringComparison.OrdinalIgnoreCase))
        {
            return "Resources/orpheus_keyword_file.ppn";
        }
        
        return $"Resources/{wakeWord.ToLowerInvariant()}_keyword_file.ppn";
    }

    private string[] GetWakeWords(string configKey, string[] defaultValue)
    {
        var configValue = _configuration[configKey];
        if (string.IsNullOrEmpty(configValue))
        {
            return defaultValue;
        }

        // Support comma-separated values
        return configValue.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim())
            .Where(w => !string.IsNullOrEmpty(w))
            .ToArray();
    }

    private int GetIntValue(string configKey, int defaultValue)
    {
        var configValue = _configuration[configKey];
        if (string.IsNullOrEmpty(configValue))
        {
            return defaultValue;
        }

        if (int.TryParse(configValue, out var parsedValue))
        {
            return parsedValue;
        }

        return defaultValue;
    }
}