# Docker Cache Persistence Guide

This document explains how to set up persistent cache storage for the Orpheus Discord bot when running in Docker containers.

## Why Cache Persistence Matters

Without persistent storage, when you update your Docker container (pull a new image), all cached MP3 files and metadata are lost. This means:
- Previously downloaded songs need to be re-downloaded
- All cache optimization benefits are lost
- Users experience slower response times for previously played tracks

## Docker Volume Setup

### Method 1: Named Volume (Recommended)

Create a named Docker volume for persistent cache storage:

```bash
# Create a named volume
docker volume create orpheus-cache

# Run the container with the named volume
docker run -d \
  --name orpheus \
  -e DISCORD_TOKEN=your_token_here \
  -v orpheus-cache:/data \
  orpheus:latest
```

### Method 2: Bind Mount

Use a local directory for cache storage:

```bash
# Create local cache directory
mkdir -p ./orpheus-data

# Run the container with bind mount
docker run -d \
  --name orpheus \
  -e DISCORD_TOKEN=your_token_here \
  -v ./orpheus-data:/data \
  orpheus:latest
```

### Method 3: Docker Compose (Recommended for Production)

Create a `docker-compose.yml` file:

```yaml
version: '3.8'

services:
  orpheus:
    image: orpheus:latest
    container_name: orpheus
    environment:
      - DISCORD_TOKEN=your_token_here
      - PICOVOICE_ACCESS_KEY=your_key_here
    volumes:
      - orpheus-cache:/data
      # Optional: Mount config file if you have custom settings
      - ./Config/appsettings.json:/app/Config/appsettings.json:ro
    restart: unless-stopped

volumes:
  orpheus-cache:
    driver: local
```

Run with:
```bash
docker-compose up -d
```

## Cache Storage Location

The cache is stored in `/data/cache/` inside the container:
- **MP3 files**: `/data/cache/*.mp3`
- **SQLite database**: `/data/cache/cache.db` (default)
- **JSON metadata**: `/data/cache/cache_metadata.json` (if using JSON storage)

## Configuration Options

You can customize cache settings via `appsettings.json`:

```json
{
  "Cache": {
    "MaxFiles": 100,
    "MaxSizeBytes": 1073741824,
    "CacheDirectory": "/data/cache",
    "StorageType": "Sqlite",
    "EnableAutomaticCleanup": true,
    "CleanupIntervalMinutes": 60
  }
}
```

### Storage Types

- **Sqlite** (default): Robust database storage, better for production
- **Json**: Simple file-based storage, easier for debugging

### Configuration Parameters

- `MaxFiles`: Maximum number of cached songs (0 = unlimited)
- `MaxSizeBytes`: Maximum cache size in bytes (default: 1GB)
- `CacheDirectory`: Where to store cache files (should be in /data for persistence)
- `StorageType`: "Sqlite" or "Json"
- `EnableAutomaticCleanup`: Whether to automatically clean up old files
- `CleanupIntervalMinutes`: How often to run cleanup (default: 60 minutes)

## Updating the Container

When you update the Orpheus container:

```bash
# Stop the old container
docker stop orpheus
docker rm orpheus

# Pull the new image
docker pull orpheus:latest

# Run with the same volume (cache persists!)
docker run -d \
  --name orpheus \
  -e DISCORD_TOKEN=your_token_here \
  -v orpheus-cache:/data \
  orpheus:latest
```

Your cache will be preserved across updates!

## Backup and Migration

### Backup Cache Data

```bash
# Create a backup of your cache volume
docker run --rm -v orpheus-cache:/data -v $(pwd):/backup alpine tar czf /backup/orpheus-cache-backup.tar.gz -C /data .
```

### Restore Cache Data

```bash
# Restore from backup
docker run --rm -v orpheus-cache:/data -v $(pwd):/backup alpine tar xzf /backup/orpheus-cache-backup.tar.gz -C /data
```

### Migrate Between Hosts

```bash
# Export volume on old host
docker run --rm -v orpheus-cache:/data -v $(pwd):/backup alpine tar czf /backup/cache.tar.gz -C /data .

# Import volume on new host  
docker volume create orpheus-cache
docker run --rm -v orpheus-cache:/data -v $(pwd):/backup alpine tar xzf /backup/cache.tar.gz -C /data
```

## Monitoring Cache

Use the bot commands to monitor cache status:

- `/cacheinfo` - View cache statistics and storage usage
- `/clearcache` - Clear entire cache (admin only)

## Troubleshooting

### Cache Not Persisting

1. Ensure you're using a volume mount: `-v orpheus-cache:/data`
2. Check that the container has write permissions to `/data`
3. Verify the volume exists: `docker volume ls`

### Permission Issues

If you encounter permission issues with bind mounts:

```bash
# Fix permissions for bind mount
sudo chown -R 65534:65534 ./orpheus-data
```

### Storage Type Migration

To migrate from JSON to SQLite storage:

1. Stop the container
2. Update your `appsettings.json` to set `"StorageType": "Sqlite"`
3. Restart the container - it will automatically recreate the cache from existing files

The bot will scan the cache directory and rebuild the metadata database from existing MP3 files.

## Best Practices

1. **Use named volumes** for production deployments
2. **Regular backups** of your cache data
3. **Monitor cache size** using `/cacheinfo` command
4. **Set appropriate limits** based on your available storage
5. **Use SQLite storage** for better reliability and performance