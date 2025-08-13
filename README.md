# Orpheus Discord Bot

## Testing

The project now includes comprehensive unit and integration tests. To run tests:

```bash
# Run all tests
dotnet test

# Run tests from solution root
cd /path/to/project
dotnet test Orpheus.sln

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"

# Use the test runner scripts
cd Orpheus.Tests
./run-tests.sh         # Linux/macOS
run-tests.bat          # Windows
```

Test coverage includes:
- Core business logic (QueuedSong, SongQueueService, BotConfiguration)
- Dependency injection setup validation
- Docker build verification
- Service integration tests

## Configuration

- Ensure your `appsettings.json` is present in the project root and contains:
  {
    "Discord": {
      "Token": "YOUR_DISCORD_TOKEN_HERE"
    }
  }

- The file must be copied to the output directory. This is handled in the .csproj file.

- You can also set the token using the `DISCORD_TOKEN` environment variable, which takes precedence over the value in `appsettings.json`.

- If you encounter a `Discord token is missing` error, check that:
  - `appsettings.json` is present in the output directory (e.g., `bin/Debug/net9.0/`)
  - The JSON is valid and the token is set
  - The environment variable is set if you prefer that method

## Troubleshooting Audio Playback Consistency

- If you experience audio speeding up, slowing down, or cutting out:
  - Ensure your server/container has enough CPU and memory available.
  - Avoid running downloads or other heavy operations at the same time as playback.
  - The bot now logs resource usage before and after playback and downloads; check logs for `[Perf]` entries to diagnose bottlenecks.
  - If running in Docker, consider increasing CPU/memory limits.
  - Network or disk I/O contention can also affect playback quality.

- If issues persist, try running the bot on a less-loaded machine or VM.

## Dependencies

- `yt-dlp` and `ffmpeg` are installed automatically in the Docker image. You do not need to manage or download these binaries in your application code.