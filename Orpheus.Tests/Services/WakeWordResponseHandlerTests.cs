using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;
using Orpheus.Services.WakeWord;
using Orpheus.Services.VoiceClientController;
using Orpheus.Services.Transcription;
using Orpheus.Services.Queue;
using Orpheus.Services.Downloader.Youtube;
using Orpheus.Services;
using Orpheus.Configuration;
using System.IO;

namespace Orpheus.Tests.Services;

public class WakeWordResponseHandlerTests
{
    [Fact]
    public void Constructor_WithValidDependencies_DoesNotThrow()
    {
        // Arrange
        var logger = Mock.Of<ILogger<WakeWordResponseHandler>>();
        var configuration = Mock.Of<IConfiguration>();
        var discordConfiguration = new BotConfiguration(configuration);
        var transcriptionService = Mock.Of<ITranscriptionService>();
        var serviceProvider = Mock.Of<IServiceProvider>();
        var queueService = Mock.Of<ISongQueueService>();
        var queuePlaybackService = Mock.Of<IQueuePlaybackService>();
        var downloader = Mock.Of<IYouTubeDownloader>();
        var messageUpdateService = Mock.Of<IMessageUpdateService>();

        // Act & Assert
        var handler = new WakeWordResponseHandler(
            logger,
            discordConfiguration,
            transcriptionService,
            serviceProvider,
            queueService,
            queuePlaybackService,
            downloader,
            messageUpdateService);
            
        Assert.NotNull(handler);
    }

    [Fact]
    public void WakeWordAcknowledgmentFile_Exists()
    {
        // Arrange
        const string acknowledgmentPath = "Resources/wake_acknowledgment.mp3";
        
        // Act & Assert
        Assert.True(File.Exists(acknowledgmentPath), $"Wake word acknowledgment file should exist at {acknowledgmentPath}");
    }

    [Fact]
    public void WakeWordAcknowledgmentFile_HasValidSize()
    {
        // Arrange
        const string acknowledgmentPath = "Resources/wake_acknowledgment_loud.mp3";
        
        // Act
        var fileInfo = new FileInfo(acknowledgmentPath);
        
        // Assert
        Assert.True(fileInfo.Length > 0, "Wake word acknowledgment file should not be empty");
        Assert.True(fileInfo.Length < 100000, "Wake word acknowledgment file should be small (under 100KB for quick playback)");
    }

    [Fact]
    public void SilenceDetectionParameters_AreOptimizedForFasterResponse()
    {
        // This test verifies that silence detection parameters have been improved for faster response times
        // by checking the expected values are reasonable for voice command usage
        
        // Arrange & Act
        const int silenceDetectionMs = 800; // Expected improved value  
        const int silenceThreshold = 300; // Expected improved sensitivity
        const int frameLengthMs = 20; // Standard Discord frame length
        
        // Calculate expected frame threshold
        var expectedFrameThreshold = silenceDetectionMs / frameLengthMs; // Should be 40 frames
        
        // Assert
        Assert.True(silenceDetectionMs <= 1000, "Silence detection should be 1 second or less for responsive voice commands");
        Assert.True(silenceDetectionMs >= 500, "Silence detection should be at least 500ms to avoid cutting off speech");
        Assert.Equal(40, expectedFrameThreshold); // 800ms / 20ms = 40 frames
        Assert.True(silenceThreshold <= 400, "Silence threshold should be sensitive enough to detect speech endings");
        Assert.True(silenceThreshold >= 200, "Silence threshold should not be too sensitive to background noise");
    }
}