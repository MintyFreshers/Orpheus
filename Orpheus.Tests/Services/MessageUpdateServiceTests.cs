using Microsoft.Extensions.Logging;
using Moq;
using Orpheus.Services;

namespace Orpheus.Tests.Services;

public class MessageUpdateServiceTests
{
    private readonly Mock<ILogger<MessageUpdateService>> _mockLogger;
    private readonly MessageUpdateService _service;

    public MessageUpdateServiceTests()
    {
        _mockLogger = new Mock<ILogger<MessageUpdateService>>();
        _service = new MessageUpdateService(_mockLogger.Object);
    }

    [Fact]
    public void MessageUpdateService_CanBeInstantiated()
    {
        // Assert
        Assert.NotNull(_service);
    }

    [Fact]
    public async Task SendSongTitleUpdateAsync_WithNonExistentSongId_DoesNotThrow()
    {
        // Arrange
        const string songId = "non-existent-song-id";
        const string actualTitle = "Some Title";

        // Act & Assert - should not throw even if song ID doesn't exist
        var exception = await Record.ExceptionAsync(async () =>
            await _service.SendSongTitleUpdateAsync(songId, actualTitle));

        Assert.Null(exception);
    }

    [Fact]
    public void RemoveInteraction_WithValidId_DoesNotThrow()
    {
        // Arrange
        const ulong interactionId = 123456789UL;

        // Act & Assert
        var exception = Record.Exception(() => _service.RemoveInteraction(interactionId));

        Assert.Null(exception);
    }

    [Fact]
    public void RemoveInteraction_WithNonExistentId_DoesNotThrow()
    {
        // Arrange
        const ulong nonExistentId = 999999999UL;

        // Act & Assert
        var exception = Record.Exception(() => _service.RemoveInteraction(nonExistentId));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("")]
    [InlineData("simple-song-id")]
    [InlineData("complex-song-id-with-dashes-and-123-numbers")]
    [InlineData("guid-like-id-12345678-1234-1234-1234-123456789012")]
    public async Task SendSongTitleUpdateAsync_WithVariousSongIds_DoesNotThrow(string songId)
    {
        // Arrange
        const string actualTitle = "Test Title";

        // Act & Assert
        var exception = await Record.ExceptionAsync(async () =>
            await _service.SendSongTitleUpdateAsync(songId, actualTitle));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(ulong.MaxValue)]
    public void RemoveInteraction_WithVariousIds_DoesNotThrow(ulong interactionId)
    {
        // Act & Assert
        var exception = Record.Exception(() => _service.RemoveInteraction(interactionId));

        Assert.Null(exception);
    }
}