# Orpheus Discord Music Bot

Orpheus is a feature-rich Discord music bot built with .NET 8.0 and the NetCord library. It supports YouTube music playback with queue management, voice commands using Whisper transcription, and wake word detection with Picovoice.

Always reference these instructions first and fallback to search or bash commands only when you encounter unexpected information that does not match the info here.

## Development Principles

### Code Quality Standards
- **Clean Code & SOLID Principles**: All code follows SOLID principles with dependency injection, single responsibility, and interface segregation
- **Consistent Structure**: Maintain consistent patterns across Commands/, Services/, Utils/, and Configuration/ directories  
- **Modular Design**: Services use dependency injection for loose coupling and easy testing/expansion
- **Fun & Engaging**: Bot should be fun and quick to use - emojis are encouraged in Discord responses 🎵🎶✨
- **Configuration Over Constants**: Use BotConfiguration class with defaults that can be overridden via config file
- **Clean Naming**: Use descriptive variable/function names that avoid deep nesting and eliminate need for comments

## Working Effectively

### Bootstrap and Build
- Install required system dependencies:
  - `sudo apt update && sudo apt install -y ffmpeg python3-pip`
  - `pip3 install --user yt-dlp` -- installs YouTube downloader tool
- Restore .NET dependencies:
  - `dotnet restore` -- takes ~1 second if up-to-date, ~23 seconds first time. NEVER CANCEL. Set timeout to 60+ seconds.
- Build the project:
  - `dotnet build --no-restore` -- takes ~1.5 seconds incremental, ~10 seconds clean build. NEVER CANCEL. Set timeout to 30+ seconds.
- Clean and rebuild if needed:
  - `dotnet clean && dotnet restore && dotnet build` -- takes ~3 seconds total after first run. NEVER CANCEL. Set timeout to 90+ seconds.

### Configuration Setup
- Copy example configuration:
  - `cp Config/appsettings.example.json Config/appsettings.json`
- Set Discord bot token (REQUIRED):
  - Environment variable: `export DISCORD_TOKEN="your_discord_bot_token_here"`
  - OR edit `Config/appsettings.json` and replace `<YOUR_DISCORD_BOT_TOKEN_HERE>` with actual token
- Set Picovoice access key (OPTIONAL - for voice commands):
  - Edit `Config/appsettings.json` and replace `<YOUR_PICOVOICE_ACCESS_KEY_HERE>` with actual key
- Environment variables take precedence over config file values

### Run the Application
- ALWAYS run the bootstrapping steps first (dependencies + build)
- Local development: `dotnet run` -- starts immediately if configured properly
- With custom token: `DISCORD_TOKEN="your_token" dotnet run`
- The bot will display masked token on startup: `Using Discord token from [source]: abcd...efgh`

### Docker Deployment
- Build Docker image: `docker build --no-cache -t orpheus .` -- takes 3-5 minutes. NEVER CANCEL. Set timeout to 10+ minutes.
- NOTE: Docker build may fail in sandboxed environments due to SSL certificate issues with PyPI
- Run container: `docker run -e DISCORD_TOKEN=your_token_here orpheus`
- The Dockerfile automatically installs ffmpeg, python3, and yt-dlp in a virtual environment

## Testing and Validation

### Current Testing State
- **No Formal Test Framework**: Project currently uses manual testing and integration testing with Discord
- **Testing Expansion Required**: Future development should add comprehensive test coverage with xUnit/NUnit
- **Manual Testing**: All functionality requires testing with real Discord server and voice channels
- **Quality Assurance**: Always test Discord bot functionality after changes to command handlers

### Build Validation
- ALWAYS test basic build process: `dotnet clean && dotnet restore && dotnet build`
- Build should complete with 0 warnings and 0 errors
- Expected output: "Build succeeded" with timing information

### Runtime Validation  
- Test configuration loading: `dotnet run` (should show token source and exit gracefully or start bot)
- Without token: Should display clear error message about missing Discord token
- With invalid token format: Should display NetCord validation error

### Manual Validation Scenarios
- ALWAYS test the Discord bot functionality after making changes to command handlers
- **Basic Commands**: Test `/ping` command (should respond with "Pong! 🏓")
- **Music Commands**: Test `/play https://www.youtube.com/watch?v=example` (requires valid Discord token and voice channel)  
- **Voice Commands**: Say "Orpheus say hello" while bot is in voice channel (requires Picovoice key)
- **Queue Management**: Test `/queue`, `/skip`, `/clearqueue` commands with emoji responses
- **Error Handling**: Verify graceful error messages with helpful emoji indicators

### Adding Test Coverage (Future Development)
When adding test frameworks:
- Use xUnit or NUnit for .NET unit testing
- Create separate test project: `Orpheus.Tests.csproj` 
- Mock Discord services and dependencies for unit testing
- Test service interfaces independently from Discord context
- Include integration tests for command workflows
- Add test CI/CD pipeline in `.github/workflows/`

### External Dependencies Verification
- Verify ffmpeg: `ffmpeg -version` -- should show version 6.1.1 or later
- Verify yt-dlp: `yt-dlp --version` -- should show current version (2025.07.21 or later)
- Verify Python: `python3 --version` -- should show Python 3.12 or later

## Architecture Overview

### Key Components
- **Program.cs**: Main entry point with dependency injection setup
- **Commands/**: Discord slash command handlers (`/play`, `/ping`, `/queue`, etc.)
- **Services/**: Core business logic (queue management, audio playback, transcription)
- **Utils/**: Helper utilities (token resolution, etc.)
- **Configuration/**: Bot configuration classes

### Core Services (Dependency Injection Pattern)
All services follow interface-based design with dependency injection for modularity:

- **ISongQueueService**: Thread-safe song queue management with events
- **IQueuePlaybackService**: Automatic queue processing and playback coordination  
- **IYouTubeDownloader**: YouTube video download using yt-dlp
- **ITranscriptionService**: Whisper-based voice transcription for voice commands
- **IVoiceClientController**: Discord voice channel connection and audio streaming management
- **IMessageUpdateService**: Discord message updating for queue status
- **IWakeWordDetectionService**: Picovoice-based wake word detection ("Orpheus")
- **BotConfiguration**: Configuration management with defaults and config overrides

### Discord Commands (Fun & Engaging Style)
Commands should be responsive and use emojis to enhance user experience:

- **Basic**: `/ping` 🏓, `/join` 🎤, `/leave` 👋, `/stop` ⏹️
- **Playback**: `/play <url-or-search>` ▶️, `/playtest` 🧪, `/resume` ⏯️  
- **Queue Management**: `/queue` 📋, `/skip` ⏭️, `/clearqueue` 🧹, `/playnext` ⏩
- **Voice Commands**: "Orpheus say hello" 🗣️ (requires Picovoice wake word detection)

### File Locations
- **Configuration**: `Config/appsettings.json` (copied from `Config/appsettings.example.json`)
- **Resources**: `Resources/ExampleTrack.mp3`, `Resources/orpheus_keyword_file.ppn`
- **Build Output**: `bin/Debug/net8.0/` (includes all dependencies and resources)
- **Documentation**: `README.md`, `QUEUE_IMPLEMENTATION.md`, `VOICE_COMMANDS.md`

## Common Development Tasks

### Adding New Commands (Following SOLID Principles)
- Create new class in `Commands/` directory inheriting from `ApplicationCommandModule<ApplicationCommandContext>`
- Use `[SlashCommand("name", "description")]` attribute
- Inject required services via constructor (dependency injection)
- Use emojis in response messages for engaging user experience
- Register automatically via dependency injection in `Program.ConfigureServices()`

### Creating New Services (Interface-First Design)
- Define interface first (e.g., `IMyService`) with clear method signatures
- Implement concrete class following single responsibility principle
- Register both interface and implementation in `Program.ConfigureServices()`
- Services registered as singletons for thread safety and performance
- Use dependency injection for all service dependencies

### Configuration Management Pattern  
- Add configuration properties to `BotConfiguration` class with sensible defaults
- Use `GetChannelId()` pattern for config values with fallback defaults
- Configuration file values override defaults: `Config/appsettings.json`
- Environment variables override config file values
- Never use hardcoded constants - always use configuration pattern

### Debugging Audio Issues
- Check ffmpeg installation: `which ffmpeg && ffmpeg -version`
- Check yt-dlp functionality: `yt-dlp --version && yt-dlp --extract-audio --audio-format mp3 "test_url"`
- Audio files downloaded to `Downloads/` folder within application directory
- Enable debug logging by building in Debug configuration

### Configuration Troubleshooting
- Token errors: Ensure Discord token is valid bot token (not user token)  
- Missing config: Verify `Config/appsettings.json` exists and is copied to build output
- Picovoice errors: Voice commands require valid Picovoice access key or will be disabled
- Use BotConfiguration class pattern for all configurable values with sensible defaults

## Code Quality Guidelines

### SOLID Principles Implementation
- **Single Responsibility**: Each service has one clear purpose (queue management, audio playback, etc.)
- **Open/Closed**: Services extend through interfaces, not modification of existing code  
- **Liskov Substitution**: All service implementations are interchangeable through their interfaces
- **Interface Segregation**: Small, focused interfaces (ISongQueueService, IYouTubeDownloader, etc.)
- **Dependency Inversion**: High-level modules depend on interfaces, not concrete implementations

### Clean Code Standards
- **Descriptive Naming**: Method and variable names should be self-documenting
- **Avoid Deep Nesting**: Use early returns and guard clauses to reduce complexity
- **No Magic Numbers**: Use configuration classes or well-named constants
- **Minimal Comments**: Code should be self-explanatory through good naming
- **Consistent Patterns**: Follow established patterns in existing codebase

### Bot Personality Guidelines
- Use emojis in Discord responses to make interactions fun and engaging 🎵🎶✨
- Keep responses concise but friendly
- Provide clear feedback for user actions
- Use consistent emoji themes (music notes for audio, tools for commands, etc.)

## Development Notes

### Architecture Patterns
- **Dependency Injection**: All services registered in `Program.ConfigureServices()` with interface-based design
- **Event-Driven**: Services communicate through events (e.g., `SongQueueService.SongAdded` event)
- **Modular Design**: Each service is self-contained and can be easily replaced or extended
- **Configuration Pattern**: BotConfiguration class handles all configurable values with defaults

### Current Testing State
- **No Formal Unit Tests**: Project currently relies on manual testing and Discord integration testing
- **Manual Testing Required**: All functionality must be tested with real Discord server after changes
- **Future Test Framework**: Consider adding xUnit/NUnit for comprehensive test coverage
- **Integration Testing**: Discord bot functionality requires live server testing

### No CI/CD Pipeline  
- No `.github/workflows/` directory exists yet
- Manual build and deployment process only
- Consider adding GitHub Actions workflows for automated testing and deployment
- Future pipeline should include build validation, test execution, and Docker image creation

### Performance Expectations
- **Restore**: ~1 second if up-to-date, ~23 seconds first time
- **Build**: ~1.5 seconds incremental, ~10 seconds clean build
- **Startup**: <2 seconds with valid configuration
- **Docker Build**: 3-10 minutes depending on network and system performance (may fail in sandboxed environments due to SSL issues)

### Known Limitations
- Docker builds may fail in environments with SSL certificate restrictions
- Voice commands require Picovoice subscription for wake word detection
- YouTube downloads require stable internet connection
- Bot requires "Connect" and "Speak" permissions in Discord voice channels

## Security Notes
- Never commit actual Discord tokens or API keys to version control
- Use environment variables or local configuration files for secrets
- The `Config/appsettings.example.json` file shows the required structure without real credentials
- Always use the example file as template and create local `appsettings.json` for development