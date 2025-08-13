# Whisper Voice Commands

This document describes how to use the Whisper transcription feature in Orpheus.

## How It Works

1. **Continuous Voice Command**: Say "Orpheus" followed immediately by your command (e.g., "Orpheus play hello world")
2. **Processing**: The bot will transcribe your complete sentence and respond accordingly
3. **Response**: The bot responds with the appropriate action or message

## Supported Commands

### Music Control Commands
- **"Orpheus play [song name/URL]"** → Searches for and queues music
  - Example: "Orpheus play pendulum propane nightmares"
  - Example: "Orpheus play https://youtube.com/watch?v=..."
- **"Orpheus playtest"** → Plays the test MP3 file
- **"Orpheus leave"** → Disconnects bot from voice channel

### Basic Commands  
- **"Orpheus say [message]"** → Bot repeats your message
  - Example: "Orpheus say hello world"
- **"Orpheus hello"** or **"Orpheus hi"** → Bot responds with greeting
- **"Orpheus ping"** → Bot responds with "Pong!"

## Example Usage

```
User: "Orpheus play pendulum propane nightmares"
Bot: "<@user> ✅ Added **Found: pendulum propane nightmares** to queue and starting playback!"
```

```
User: "Orpheus leave"
Bot: "<@user> Left the voice channel."
```

```
User: "Orpheus playtest"
Bot: "<@user> Now playing test track in your voice channel."
```

```
User: "Orpheus say hello everyone"
Bot: "<@user> hello everyone"
```

## Technical Details

- The bot buffers the last 3 seconds of audio to capture continuous speech
- When "Orpheus" is detected, the buffered audio plus subsequent audio is transcribed
- Commands are processed within an 8-second window after wake word detection
- No need to wait for a response - speak your complete command in one sentence

## Technical Requirements

- The bot must be in a voice channel (use `/join` command)
- Requires Picovoice access key for wake word detection
- Whisper model will be downloaded automatically on first use (~40MB for tiny model)

## Configuration

The feature requires minimal configuration:

### Required Configuration
- Discord bot token in `appsettings.json`
- Picovoice access key for wake word detection
- Default channel ID and guild ID in configuration

### Configuration Example
```json
{
  "Discord": {
    "Token": "YOUR_DISCORD_BOT_TOKEN",
    "DefaultChannelId": "YOUR_CHANNEL_ID",
    "DefaultGuildId": "YOUR_GUILD_ID"
  },
  "PicovoiceAccessKey": "YOUR_PICOVOICE_ACCESS_KEY"
}
```

### Auto-Configuration
- Whisper model downloads automatically on first use (~40MB for tiny model)
- 8-second timeout for voice commands after wake word detection
- 3-second audio buffer to capture continuous speech

## Implementation Notes

Voice commands are processed by the `VoiceCommandProcessor` service which:
- Parses transcribed speech into actionable commands
- Extracts parameters (like song names) from voice input
- Integrates with existing Discord slash command functionality
- Provides user feedback for all operations