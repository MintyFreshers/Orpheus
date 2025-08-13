using Microsoft.Extensions.Configuration;
using Moq;
using Orpheus.Configuration;

namespace Orpheus.Tests.Configuration;

public class BotConfigurationTests
{
    [Fact]
    public void DefaultChannelId_WithValidConfigValue_ReturnsConfigValue()
    {
        // Arrange
        const string configKey = "Discord:DefaultChannelId";
        const ulong expectedChannelId = 987654321UL;
        var mockConfiguration = new Mock<IConfiguration>();
        mockConfiguration.Setup(c => c[configKey]).Returns(expectedChannelId.ToString());
        
        var botConfiguration = new BotConfiguration(mockConfiguration.Object);

        // Act
        var result = botConfiguration.DefaultChannelId;

        // Assert
        Assert.Equal(expectedChannelId, result);
    }

    [Fact]
    public void DefaultChannelId_WithNullConfigValue_ReturnsDefaultValue()
    {
        // Arrange
        const string configKey = "Discord:DefaultChannelId";
        const ulong defaultChannelId = 738893202706268292UL;
        var mockConfiguration = new Mock<IConfiguration>();
        mockConfiguration.Setup(c => c[configKey]).Returns((string?)null);
        
        var botConfiguration = new BotConfiguration(mockConfiguration.Object);

        // Act
        var result = botConfiguration.DefaultChannelId;

        // Assert
        Assert.Equal(defaultChannelId, result);
    }

    [Fact]
    public void DefaultChannelId_WithEmptyConfigValue_ReturnsDefaultValue()
    {
        // Arrange
        const string configKey = "Discord:DefaultChannelId";
        const ulong defaultChannelId = 738893202706268292UL;
        var mockConfiguration = new Mock<IConfiguration>();
        mockConfiguration.Setup(c => c[configKey]).Returns(string.Empty);
        
        var botConfiguration = new BotConfiguration(mockConfiguration.Object);

        // Act
        var result = botConfiguration.DefaultChannelId;

        // Assert
        Assert.Equal(defaultChannelId, result);
    }

    [Fact]
    public void DefaultChannelId_WithInvalidConfigValue_ReturnsDefaultValue()
    {
        // Arrange
        const string configKey = "Discord:DefaultChannelId";
        const ulong defaultChannelId = 738893202706268292UL;
        var mockConfiguration = new Mock<IConfiguration>();
        mockConfiguration.Setup(c => c[configKey]).Returns("not-a-number");
        
        var botConfiguration = new BotConfiguration(mockConfiguration.Object);

        // Act
        var result = botConfiguration.DefaultChannelId;

        // Assert
        Assert.Equal(defaultChannelId, result);
    }

    [Fact]
    public void DefaultChannelId_WithWhitespaceConfigValue_ReturnsDefaultValue()
    {
        // Arrange
        const string configKey = "Discord:DefaultChannelId";
        const ulong defaultChannelId = 738893202706268292UL;
        var mockConfiguration = new Mock<IConfiguration>();
        mockConfiguration.Setup(c => c[configKey]).Returns("   ");
        
        var botConfiguration = new BotConfiguration(mockConfiguration.Object);

        // Act
        var result = botConfiguration.DefaultChannelId;

        // Assert
        Assert.Equal(defaultChannelId, result);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("18446744073709551615")] // ulong.MaxValue
    public void DefaultChannelId_WithValidNumericStrings_ParsesCorrectly(string configValue)
    {
        // Arrange
        const string configKey = "Discord:DefaultChannelId";
        var expectedChannelId = ulong.Parse(configValue);
        var mockConfiguration = new Mock<IConfiguration>();
        mockConfiguration.Setup(c => c[configKey]).Returns(configValue);
        
        var botConfiguration = new BotConfiguration(mockConfiguration.Object);

        // Act
        var result = botConfiguration.DefaultChannelId;

        // Assert
        Assert.Equal(expectedChannelId, result);
    }
}