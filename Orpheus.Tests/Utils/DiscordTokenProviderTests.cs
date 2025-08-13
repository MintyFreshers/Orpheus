using Microsoft.Extensions.Configuration;
using Moq;
using Orpheus.Utils;

namespace Orpheus.Tests.Utils;

public class DiscordTokenProviderTests
{
    [Fact]
    public void ResolveToken_WithEnvironmentVariable_ReturnsEnvironmentToken()
    {
        // Arrange
        const string expectedToken = "env_token_12345";
        var mockConfiguration = new Mock<IConfiguration>();
        mockConfiguration.Setup(c => c["DISCORD_TOKEN"]).Returns(expectedToken);
        mockConfiguration.Setup(c => c["Discord:Token"]).Returns("config_token");

        // Act
        var result = DiscordTokenProvider.ResolveToken(mockConfiguration.Object, out var tokenSource);

        // Assert
        Assert.Equal(expectedToken, result);
        Assert.Equal("environment variable DISCORD_TOKEN", tokenSource);
    }

    [Fact]
    public void ResolveToken_WithConfigurationOnly_ReturnsConfigToken()
    {
        // Arrange
        const string expectedToken = "config_token_12345";
        var mockConfiguration = new Mock<IConfiguration>();
        mockConfiguration.Setup(c => c["DISCORD_TOKEN"]).Returns((string?)null);
        mockConfiguration.Setup(c => c["Discord:Token"]).Returns(expectedToken);

        // Act
        var result = DiscordTokenProvider.ResolveToken(mockConfiguration.Object, out var tokenSource);

        // Assert
        Assert.Equal(expectedToken, result);
        Assert.Equal("appsettings.json (Discord:Token)", tokenSource);
    }

    [Fact]
    public void ResolveToken_WithEmptyEnvironmentVariable_FallsBackToConfig()
    {
        // Arrange
        const string expectedToken = "config_token_12345";
        var mockConfiguration = new Mock<IConfiguration>();
        mockConfiguration.Setup(c => c["DISCORD_TOKEN"]).Returns("");
        mockConfiguration.Setup(c => c["Discord:Token"]).Returns(expectedToken);

        // Act
        var result = DiscordTokenProvider.ResolveToken(mockConfiguration.Object, out var tokenSource);

        // Assert
        Assert.Equal(expectedToken, result);
        Assert.Equal("appsettings.json (Discord:Token)", tokenSource);
    }

    [Fact]
    public void ResolveToken_WithWhitespaceEnvironmentVariable_FallsBackToConfig()
    {
        // Arrange
        const string expectedToken = "config_token_12345";
        var mockConfiguration = new Mock<IConfiguration>();
        mockConfiguration.Setup(c => c["DISCORD_TOKEN"]).Returns("   ");
        mockConfiguration.Setup(c => c["Discord:Token"]).Returns(expectedToken);

        // Act
        var result = DiscordTokenProvider.ResolveToken(mockConfiguration.Object, out var tokenSource);

        // Assert
        Assert.Equal(expectedToken, result);
        Assert.Equal("appsettings.json (Discord:Token)", tokenSource);
    }

    [Fact]
    public void ResolveToken_WithNoTokensAvailable_ThrowsException()
    {
        // Arrange
        var mockConfiguration = new Mock<IConfiguration>();
        mockConfiguration.Setup(c => c["DISCORD_TOKEN"]).Returns((string?)null);
        mockConfiguration.Setup(c => c["Discord:Token"]).Returns((string?)null);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => 
            DiscordTokenProvider.ResolveToken(mockConfiguration.Object, out _));

        Assert.Equal("Discord token is missing. Set DISCORD_TOKEN env variable or Discord:Token in appsettings.json.", exception.Message);
    }

    [Fact]
    public void ResolveToken_WithEmptyConfigTokens_ThrowsException()
    {
        // Arrange
        var mockConfiguration = new Mock<IConfiguration>();
        mockConfiguration.Setup(c => c["DISCORD_TOKEN"]).Returns("");
        mockConfiguration.Setup(c => c["Discord:Token"]).Returns("");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => 
            DiscordTokenProvider.ResolveToken(mockConfiguration.Object, out _));

        Assert.Equal("Discord token is missing. Set DISCORD_TOKEN env variable or Discord:Token in appsettings.json.", exception.Message);
    }

    [Fact]
    public void MaskToken_WithValidToken_ReturnsPartiallyMaskedToken()
    {
        // Arrange
        const string token = "abc123defghij456";

        // Act
        var result = DiscordTokenProvider.MaskToken(token);

        // Assert
        Assert.Equal("abc1...j456", result);
    }

    [Fact]
    public void MaskToken_WithShortToken_ReturnsMaskedString()
    {
        // Arrange
        const string token = "short";

        // Act
        var result = DiscordTokenProvider.MaskToken(token);

        // Assert
        Assert.Equal("****", result);
    }

    [Fact]
    public void MaskToken_WithEmptyToken_ReturnsMaskedString()
    {
        // Act
        var result = DiscordTokenProvider.MaskToken("");

        // Assert
        Assert.Equal("****", result);
    }

    [Fact]
    public void MaskToken_WithNullToken_ReturnsMaskedString()
    {
        // Act
        var result = DiscordTokenProvider.MaskToken(null!);

        // Assert
        Assert.Equal("****", result);
    }

    [Theory]
    [InlineData("12345678", "1234...5678")]
    [InlineData("abcdefghijk", "abcd...hijk")]
    [InlineData("discord_token_very_long_123456", "disc...3456")]
    public void MaskToken_WithVariousLengths_ReturnsCorrectMask(string token, string expected)
    {
        // Act
        var result = DiscordTokenProvider.MaskToken(token);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void MaskToken_WithExactlyEightCharacters_ReturnsCorrectMask()
    {
        // Arrange
        const string token = "12345678";

        // Act
        var result = DiscordTokenProvider.MaskToken(token);

        // Assert
        Assert.Equal("1234...5678", result);
    }
}