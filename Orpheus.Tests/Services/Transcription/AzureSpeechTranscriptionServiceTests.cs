using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Orpheus.Services.Transcription;

namespace Orpheus.Tests.Services.Transcription;

public class AzureSpeechTranscriptionServiceTests
{
    private readonly Mock<ILogger<AzureSpeechTranscriptionService>> _mockLogger;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly AzureSpeechTranscriptionService _service;

    public AzureSpeechTranscriptionServiceTests()
    {
        _mockLogger = new Mock<ILogger<AzureSpeechTranscriptionService>>();
        _mockConfiguration = new Mock<IConfiguration>();
        _service = new AzureSpeechTranscriptionService(_mockLogger.Object, _mockConfiguration.Object);
    }

    [Fact]
    public void Constructor_SetsPropertiesCorrectly()
    {
        // Assert
        Assert.False(_service.IsInitialized);
    }

    [Fact]
    public async Task InitializeAsync_WithMissingSubscriptionKey_ThrowsInvalidOperationException()
    {
        // Arrange
        _mockConfiguration.Setup(c => c["AzureSpeech:SubscriptionKey"]).Returns((string?)null);
        
        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.InitializeAsync());
        Assert.Contains("Azure Speech subscription key is missing", exception.Message);
    }

    [Fact]
    public async Task TranscribeAudioAsync_WhenNotInitialized_ReturnsNull()
    {
        // Arrange
        var audioData = new byte[] { 1, 2, 3, 4 };

        // Act
        var result = await _service.TranscribeAudioAsync(audioData);

        // Assert
        Assert.Null(result);
        
        // Verify warning was logged
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Transcription service not initialized")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void Cleanup_DoesNotThrow()
    {
        // Act & Assert
        _service.Cleanup(); // Should not throw
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        // Act & Assert
        _service.Dispose(); // Should not throw
    }
}