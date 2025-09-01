# Voice Commands

# Voice Commands

This document describes how to use the voice commands feature in Orpheus, including the improved wake word detection system.

## How It Works

1. **Wake Word Detection**: Say one of the supported wake words: **"Computer"** (recommended), **"Orpheus"**, **"Jarvis"**, or other configured keywords
2. **Voice Command**: Follow immediately with your command (e.g., "Computer play hello world")  
3. **Processing**: The bot transcribes your complete sentence using Azure Speech Service and responds accordingly
4. **Response**: The bot responds with the appropriate action or message

## Wake Words

### Recommended Wake Words (Easy Recognition)
- **"Computer"** → Primary recommended wake word - most reliable
- **"Jarvis"** → AI assistant themed, very reliable  
- **"Bumblebee"** → Fun and distinctive

### Available Wake Words
The bot supports these built-in wake words with excellent recognition accuracy:
- `computer` (recommended - easiest to recognize)
- `jarvis` (AI assistant theme)
- `bumblebee` (distinctive)
- `alexa` (familiar)
- `porcupine` (library name)
- `americano` (coffee theme)
- `blueberry` (fruit theme)
- `grapefruit` (fruit theme)
- `grasshopper` (nature theme)
- `hey google` (Google Assistant style)
- `hey siri` (Siri style)
- `ok google` (Google Assistant style)
- `picovoice` (company name)
- `terminator` (movie reference)

### Legacy Wake Words
- **"Orpheus"** → Original custom wake word (still supported but less reliable)

## Supported Commands

### Music Control Commands
- **"[wake word] play [song name/URL]"** → Searches for and queues music
  - Example: "Computer play pendulum propane nightmares"
  - Example: "Jarvis play https://youtube.com/watch?v=..."
- **"[wake word] playtest"** → Plays the test MP3 file
- **"[wake word] leave"** → Disconnects bot from voice channel

### Basic Commands  
- **"[wake word] say [message]"** → Bot repeats your message
  - Example: "Computer say hello world"
- **"[wake word] hello"** or **"[wake word] hi"** → Bot responds with greeting
- **"[wake word] ping"** → Bot responds with "Pong!"

## Example Usage

```
User: "Computer play pendulum propane nightmares"
Bot: "<@user> ✅ Added **Found: pendulum propane nightmares** to queue and starting playback!"
```

```
User: "Jarvis leave"
Bot: "<@user> Left the voice channel."
```

```
User: "Computer playtest"
Bot: "<@user> Now playing test track in your voice channel."
```

```
User: "Computer say hello everyone"
Bot: "<@user> hello everyone"
```

## Technical Details

- The bot buffers the last 3 seconds of audio to capture continuous speech
- When any configured wake word is detected, the buffered audio plus subsequent audio is transcribed using **Azure Cognitive Services Speech-to-Text**
- Commands are processed within an 8-second window after wake word detection  
- No need to wait for a response - speak your complete command in one sentence
- **Improved performance**: Azure Speech Service provides faster response times and better accuracy compared to the previous Whisper implementation
- **Enhanced Wake Word Recognition**: Built-in Porcupine keywords provide much better accuracy than custom models

## Technical Requirements

- The bot must be in a voice channel (use `/join` command)
- Requires Picovoice access key for wake word detection
- **Azure Speech Service subscription key and region** (replaces Whisper model requirement)

## Configuration

### Wake Word Configuration
The wake word system is highly configurable for optimal recognition:

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

### Complete Configuration Example
```json
{
  "Discord": {
    "Token": "YOUR_DISCORD_BOT_TOKEN",
    "DefaultChannelId": "YOUR_CHANNEL_ID"
  },
  "PicovoiceAccessKey": "YOUR_PICOVOICE_ACCESS_KEY",
  "WakeWord": {
    "EnabledWords": "computer,jarvis",
    "DetectionCooldownMs": 3000,
    "Sensitivities": {
      "computer": 0.6,
      "jarvis": 0.6
    }
  },
  "AzureSpeech": {
    "SubscriptionKey": "YOUR_AZURE_SPEECH_SUBSCRIPTION_KEY",
    "Region": "eastus"
  }
}
```

### Auto-Configuration
- No model downloads required (cloud-based service)
- Guild context automatically determined from user's voice channel location
- 8-second timeout for voice commands after wake word detection
- 3-second audio buffer to capture continuous speech
- **Default wake words**: "computer" and "orpheus" if not configured
- **Smart sensitivity defaults**: Built-in keywords use 0.6, custom keywords use 0.7

## Wake Word Tuning Tips

### For Better Recognition
- **Use built-in keywords**: "computer", "jarvis", "bumblebee" work better than custom models
- **Lower sensitivity values** (0.4-0.6): More sensitive, detects more easily but may have false positives
- **Speak clearly**: Pause briefly after the wake word before your command

### For Fewer False Positives  
- **Higher sensitivity values** (0.7-0.9): Less sensitive, fewer false positives but might miss real detections
- **Increase cooldown**: Set `DetectionCooldownMs` to 5000+ to prevent rapid repeated detections
- **Choose distinctive words**: "grasshopper", "bumblebee" are less likely to be said accidentally

## Implementation Notes

Voice commands are processed by the `VoiceCommandProcessor` service and `WakeWordResponseHandler` which:
- Parse transcribed speech into actionable commands using **Azure Speech Service**
- Extract parameters (like song names) from voice input
- Automatically determine guild context from user's current voice channel
- Integrate with existing Discord slash command functionality
- Provide user feedback for all operations

## Technical Architecture

The voice command system uses a two-tier approach:
- **Basic Commands**: Handled by `VoiceCommandProcessor` (say, hello, ping)
- **Advanced Commands**: Handled by `WakeWordResponseHandler` (play, leave, playtest)

**Transcription Service**: `AzureSpeechTranscriptionService` implements `ITranscriptionService` interface, providing:
- Real-time speech recognition
- Optimized for voice commands and low latency
- Better accuracy than previous Whisper implementation
- Significantly faster response times

This architecture avoids circular dependencies while maintaining full functionality.