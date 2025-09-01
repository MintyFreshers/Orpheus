# Wake Word Recognition Improvements

This document describes the enhanced wake word detection system that addresses poor recognition accuracy by supporting multiple wake words and configurable sensitivity.

## Overview of Changes

The improved wake word system now supports:

1. **Multiple Wake Words**: Configure multiple wake words simultaneously
2. **Built-in Keywords**: Use proven Porcupine built-in keywords for better accuracy
3. **Configurable Sensitivity**: Fine-tune sensitivity per wake word
4. **Easy-to-Recognize Words**: "computer" as the primary easy wake word (instead of "botty")
5. **Backward Compatibility**: "orpheus" still works as a secondary wake word

## Configuration

### Example Configuration (appsettings.json)

```json
{
  "WakeWord": {
    "EnabledWords": "computer,orpheus",
    "DetectionCooldownMs": 3000,
    "Sensitivities": {
      "computer": 0.6,
      "orpheus": 0.7,
      "jarvis": 0.6,
      "bumblebee": 0.6
    },
    "CustomModels": {
      "orpheus": "Resources/orpheus_keyword_file.ppn"
    }
  }
}
```

### Configuration Options

#### `WakeWord:EnabledWords`
- **Type**: Comma-separated string
- **Default**: `"computer,orpheus"`
- **Description**: List of wake words to activate
- **Examples**: 
  - `"computer"` - Only computer wake word
  - `"computer,jarvis"` - Multiple built-in wake words
  - `"computer,orpheus"` - Mix of built-in and custom

#### `WakeWord:DetectionCooldownMs`
- **Type**: Integer (milliseconds)
- **Default**: `3000` (3 seconds)
- **Description**: Minimum time between wake word detections from the same user

#### `WakeWord:Sensitivities:{wakeword}`
- **Type**: Float (0.1 to 1.0)
- **Default Values**:
  - `computer`: 0.6 (more sensitive, easier recognition)
  - `orpheus`: 0.7 (less sensitive due to custom model)
  - Built-in keywords: 0.6
- **Description**: Higher values = less sensitive (fewer false positives, might miss real wake words)

#### `WakeWord:CustomModels:{wakeword}`
- **Type**: File path string
- **Default**: `"Resources/orpheus_keyword_file.ppn"` for "orpheus"
- **Description**: Path to custom .ppn model files for non-built-in keywords

## Available Built-in Wake Words

The following wake words are built into Porcupine and provide excellent recognition accuracy:

| Wake Word | Enum Value | Recommended Use |
|-----------|------------|-----------------|
| `alexa` | `ALEXA` | Familiar wake word |
| `computer` | `COMPUTER` | **Recommended primary** - easy to say and recognize |
| `jarvis` | `JARVIS` | Popular AI assistant name |
| `bumblebee` | `BUMBLEBEE` | Fun, distinctive word |
| `porcupine` | `PORCUPINE` | Library's own wake word |
| `americano` | `AMERICANO` | Coffee-themed word |
| `blueberry` | `BLUEBERRY` | Fruit-themed word |
| `grapefruit` | `GRAPEFRUIT` | Fruit-themed word |
| `grasshopper` | `GRASSHOPPER` | Animal-themed word |
| `hey google` | `HEY_GOOGLE` | Google Assistant style |
| `hey siri` | `HEY_SIRI` | Siri style |
| `ok google` | `OK_GOOGLE` | Google Assistant style |
| `picovoice` | `PICOVOICE` | Company name |
| `terminator` | `TERMINATOR` | Movie reference |

## Technical Implementation

### Service Architecture

- **`ImprovedPicovoiceWakeWordService`**: New wake word detection service
- **`WakeWordConfiguration`**: Configuration management class
- **Backward Compatibility**: Still implements `IWakeWordDetectionService` interface

### Key Improvements

1. **Built-in Keyword Priority**: When both built-in and custom keywords are configured, built-in keywords take priority for better reliability
2. **Enhanced Logging**: Shows which specific wake word was detected
3. **Robust Error Handling**: Graceful fallback when model files are missing
4. **Configuration Validation**: Validates sensitivity ranges and file paths

### Migration from Original Service

The dependency injection registration has been updated in `Program.cs`:

```csharp
// Old (single wake word, custom model only)
services.AddSingleton<IWakeWordDetectionService, PicovoiceWakeWordService>();

// New (multiple wake words, built-in + custom support)
services.AddSingleton<IWakeWordDetectionService, ImprovedPicovoiceWakeWordService>();
services.AddSingleton<WakeWordConfiguration>();
```

## Usage Examples

### Basic Usage (Recommended)
Set up the default "computer" wake word with good sensitivity:

```json
{
  "WakeWord": {
    "EnabledWords": "computer",
    "Sensitivities": {
      "computer": 0.6
    }
  }
}
```

**Voice Command**: "Computer, play some music"

### Multiple Wake Words
Configure both easy and custom wake words:

```json
{
  "WakeWord": {
    "EnabledWords": "computer,orpheus",
    "Sensitivities": {
      "computer": 0.6,
      "orpheus": 0.7
    }
  }
}
```

**Voice Commands**: 
- "Computer, skip this song"
- "Orpheus, join voice channel"

### Gaming Theme
Set up gaming-friendly wake words:

```json
{
  "WakeWord": {
    "EnabledWords": "jarvis,computer",
    "Sensitivities": {
      "jarvis": 0.6,
      "computer": 0.6
    }
  }
}
```

## Troubleshooting

### Wake Word Not Detected
1. **Check Sensitivity**: Lower the sensitivity value (e.g., from 0.7 to 0.5)
2. **Try Built-in Keywords**: Use "computer" instead of custom keywords
3. **Check Audio Quality**: Ensure clear microphone input
4. **Verify Configuration**: Check that `EnabledWords` includes your desired wake word

### False Positives
1. **Increase Sensitivity**: Higher values (e.g., 0.8) reduce false positives
2. **Increase Cooldown**: Extend `DetectionCooldownMs` to 5000+ milliseconds
3. **Choose Distinctive Wake Words**: Use words like "bumblebee" or "grasshopper"

### Custom Model Issues
1. **Check File Path**: Verify `.ppn` file exists at specified path
2. **Use Built-in Alternative**: Switch to built-in keywords for better reliability
3. **Check Logs**: Look for model loading errors in application logs

## Performance Impact

- **Minimal Memory Overhead**: Built-in keywords use less memory than custom models
- **Better CPU Efficiency**: Optimized detection algorithms in Porcupine 3.0
- **Reduced I/O**: No need to load multiple .ppn files for built-in keywords

## Future Enhancements

Potential improvements for future versions:
- Voice activity detection to reduce false positives
- User-specific sensitivity settings
- Wake word confidence scoring
- Multi-language wake word support