using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using Orpheus.Configuration;
using Orpheus.Services;
using Orpheus.Services.Cache;
using Orpheus.Services.Downloader.Youtube;
using Orpheus.Services.Queue;
using Orpheus.Services.VoiceClientController;
using Orpheus.Services.WakeWord;
using Orpheus.Services.Transcription;
using Orpheus.Utils;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var configuration = BuildConfiguration();
        var token = DiscordTokenProvider.ResolveToken(configuration, out var tokenSource);
        Console.WriteLine($"[Startup] Using Discord token from {tokenSource}: {DiscordTokenProvider.MaskToken(token)}");
        var host = CreateHostBuilder(args, token).Build();
        RegisterModules(host);
        await host.RunAsync();
    }

    private static IConfiguration BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .AddJsonFile(GetConfigurationPath(), optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();
    }

    private static string GetConfigurationPath()
    {
        // Priority order: /data/appsettings.json -> Config/appsettings.json -> create default in /data
        const string dataConfigPath = "/data/appsettings.json";
        const string localConfigPath = "Config/appsettings.json";
        const string exampleConfigPath = "Config/appsettings.example.json";
        
        if (File.Exists(dataConfigPath))
        {
            Console.WriteLine($"[Config] Using configuration from: {dataConfigPath}");
            return dataConfigPath;
        }
        
        if (File.Exists(localConfigPath))
        {
            Console.WriteLine($"[Config] Using configuration from: {localConfigPath}");
            return localConfigPath;
        }
        
        // Create default config in /data if it doesn't exist and we have the example
        if (Directory.Exists("/data"))
        {
            Console.WriteLine($"[Config] /data directory exists, checking for example config...");
            
            if (File.Exists(exampleConfigPath))
            {
                try
                {
                    Console.WriteLine($"[Config] Copying {exampleConfigPath} to {dataConfigPath}...");
                    
                    // Ensure we can write to /data directory
                    var testFile = "/data/.write_test";
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);
                    
                    File.Copy(exampleConfigPath, dataConfigPath);
                    Console.WriteLine($"[Config] Successfully created default configuration at: {dataConfigPath}");
                    Console.WriteLine("[Config] Please edit /data/appsettings.json with your Discord token and other settings");
                    return dataConfigPath;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Config] Error: Could not create default config at {dataConfigPath}: {ex.Message}");
                    Console.WriteLine($"[Config] Exception type: {ex.GetType().Name}");
                }
            }
            else
            {
                Console.WriteLine($"[Config] Warning: Example config file not found at {exampleConfigPath}");
            }
        }
        else
        {
            Console.WriteLine("[Config] /data directory does not exist, using local config");
        }
        
        // Fall back to local config path
        Console.WriteLine($"[Config] Using configuration from: {localConfigPath} (fallback)");
        return localConfigPath;
    }

    private static IHostBuilder CreateHostBuilder(string[] args, string token)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostingContext, configBuilder) =>
            {
                var configPath = GetConfigurationPath();
                configBuilder.AddJsonFile(configPath, optional: true, reloadOnChange: true);
                configBuilder.AddEnvironmentVariables();
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
#if DEBUG
                logging.SetMinimumLevel(LogLevel.Debug);
#endif
            })
            .ConfigureServices(ConfigureServices)
            .UseDiscordGateway(options =>
            {
                options.Token = token;
            })
            .UseApplicationCommands();
    }

    private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        services.AddLogging();
        
        // Cache configuration and services
        services.Configure<CacheConfiguration>(context.Configuration.GetSection("Cache"));
        services.AddSingleton<CacheConfiguration>(provider =>
        {
            var options = new CacheConfiguration();
            context.Configuration.GetSection("Cache").Bind(options);
            return options;
        });
        services.AddSingleton<ICacheService>(provider =>
        {
            var config = provider.GetRequiredService<CacheConfiguration>();
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            
            return config.StorageType switch
            {
                CacheStorageType.Sqlite => new SqliteMp3CacheService(config, loggerFactory.CreateLogger<SqliteMp3CacheService>()),
                CacheStorageType.Json => new Mp3CacheService(config, loggerFactory.CreateLogger<Mp3CacheService>()),
                _ => new SqliteMp3CacheService(config, loggerFactory.CreateLogger<SqliteMp3CacheService>())
            };
        });
        services.AddHostedService<CacheCleanupService>();
        
        // YouTube downloader with caching
        services.AddSingleton<YouTubeDownloaderService>(); // Base downloader
        services.AddSingleton<IYouTubeDownloader>(provider =>
        {
            var baseDownloader = provider.GetRequiredService<YouTubeDownloaderService>();
            var cacheService = provider.GetRequiredService<ICacheService>();
            var logger = provider.GetRequiredService<ILogger<CachedYouTubeDownloaderService>>();
            return new CachedYouTubeDownloaderService(baseDownloader, cacheService, logger);
        });
        
        services.AddSingleton<ISongQueueService, SongQueueService>();
        services.AddSingleton<IQueuePlaybackService, QueuePlaybackService>();
        services.AddSingleton<BackgroundDownloadService>();
        services.AddSingleton<IBackgroundDownloadService>(provider => provider.GetRequiredService<BackgroundDownloadService>());
        services.AddHostedService<BackgroundDownloadService>(provider => provider.GetRequiredService<BackgroundDownloadService>());
        services.AddSingleton<IAudioPlaybackService, AudioPlaybackService>();
        services.AddSingleton<IVoiceClientController, VoiceClientController>();
        services.AddSingleton<IWakeWordDetectionService, PicovoiceWakeWordService>();
        services.AddSingleton<ITranscriptionService, WhisperTranscriptionService>();
        services.AddSingleton<IVoiceCommandProcessor, VoiceCommandProcessor>();
        services.AddSingleton<BotConfiguration>();
        services.AddSingleton<WakeWordResponseHandler>();
        services.AddSingleton<IMessageUpdateService, MessageUpdateService>();
    }

    private static void RegisterModules(IHost host)
    {
        host.AddModules(typeof(Program).Assembly);
        host.UseGatewayEventHandlers();
        
        // Initialize transcription service
        var transcriptionService = host.Services.GetRequiredService<ITranscriptionService>();
        _ = transcriptionService.InitializeAsync();
    }
}