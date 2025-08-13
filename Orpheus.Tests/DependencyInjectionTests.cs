using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orpheus.Configuration;
using Orpheus.Services;
using Orpheus.Services.Downloader.Youtube;
using Orpheus.Services.Queue;
using Orpheus.Services.Transcription;
using Orpheus.Services.VoiceClientController;
using Orpheus.Services.WakeWord;

namespace Orpheus.Tests.Integration;

public class DependencyInjectionTests
{
    [Fact]
    public void ServiceCollection_RegistersAllRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        var context = new HostBuilderContext(new Dictionary<object, object>())
        {
            Configuration = configuration
        };

        // Simulate the ConfigureServices method from Program.cs
        services.AddLogging();
        services.AddSingleton<IYouTubeDownloader, YouTubeDownloaderService>();
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
        
        // Add required configuration for services
        services.AddSingleton<IConfiguration>(configuration);

        // Act
        var serviceProvider = services.BuildServiceProvider();

        // Assert - Verify all services can be resolved
        Assert.NotNull(serviceProvider.GetRequiredService<IYouTubeDownloader>());
        Assert.NotNull(serviceProvider.GetRequiredService<ISongQueueService>());
        Assert.NotNull(serviceProvider.GetRequiredService<IQueuePlaybackService>());
        Assert.NotNull(serviceProvider.GetRequiredService<BackgroundDownloadService>());
        Assert.NotNull(serviceProvider.GetRequiredService<IBackgroundDownloadService>());
        Assert.NotNull(serviceProvider.GetRequiredService<IAudioPlaybackService>());
        Assert.NotNull(serviceProvider.GetRequiredService<IVoiceClientController>());
        Assert.NotNull(serviceProvider.GetRequiredService<IWakeWordDetectionService>());
        Assert.NotNull(serviceProvider.GetRequiredService<ITranscriptionService>());
        Assert.NotNull(serviceProvider.GetRequiredService<IVoiceCommandProcessor>());
        Assert.NotNull(serviceProvider.GetRequiredService<BotConfiguration>());
        Assert.NotNull(serviceProvider.GetRequiredService<WakeWordResponseHandler>());
        Assert.NotNull(serviceProvider.GetRequiredService<IMessageUpdateService>());
    }

    [Fact]
    public void ServiceCollection_BackgroundDownloadService_RegisteredAsBothTypesAndHostedService()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        // Add all required dependencies for BackgroundDownloadService
        services.AddLogging();
        services.AddSingleton<ISongQueueService, SongQueueService>();
        services.AddSingleton<IYouTubeDownloader, YouTubeDownloaderService>();
        services.AddSingleton<IMessageUpdateService, MessageUpdateService>();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<BackgroundDownloadService>();
        services.AddSingleton<IBackgroundDownloadService>(provider => provider.GetRequiredService<BackgroundDownloadService>());
        services.AddHostedService<BackgroundDownloadService>(provider => provider.GetRequiredService<BackgroundDownloadService>());

        // Act
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var backgroundService = serviceProvider.GetRequiredService<BackgroundDownloadService>();
        var backgroundInterface = serviceProvider.GetRequiredService<IBackgroundDownloadService>();
        var hostedServices = serviceProvider.GetServices<IHostedService>();

        Assert.NotNull(backgroundService);
        Assert.NotNull(backgroundInterface);
        Assert.Same(backgroundService, backgroundInterface);
        Assert.Contains(hostedServices, service => service == backgroundService);
    }

    [Fact]
    public void ServiceCollection_AllServicesAreSingletons()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        // Add all services as singletons
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<ISongQueueService, SongQueueService>();
        services.AddSingleton<BotConfiguration>();

        // Act
        var serviceProvider = services.BuildServiceProvider();

        // Assert - Verify singleton behavior
        var queueService1 = serviceProvider.GetRequiredService<ISongQueueService>();
        var queueService2 = serviceProvider.GetRequiredService<ISongQueueService>();
        Assert.Same(queueService1, queueService2);

        var botConfig1 = serviceProvider.GetRequiredService<BotConfiguration>();
        var botConfig2 = serviceProvider.GetRequiredService<BotConfiguration>();
        Assert.Same(botConfig1, botConfig2);
    }

    [Fact]
    public void ServiceCollection_LoggerIsAvailable()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var logger = serviceProvider.GetRequiredService<ILogger<DependencyInjectionTests>>();
        Assert.NotNull(logger);
    }

    [Fact]
    public void BotConfiguration_CanBeResolvedWithConfiguration()
    {
        // Arrange
        var configData = new Dictionary<string, string>
        {
            { "Discord:DefaultChannelId", "123456789" }
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData!)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<BotConfiguration>();

        // Act
        var serviceProvider = services.BuildServiceProvider();
        var botConfig = serviceProvider.GetRequiredService<BotConfiguration>();

        // Assert
        Assert.NotNull(botConfig);
        Assert.Equal(123456789UL, botConfig.DefaultChannelId);
    }
}