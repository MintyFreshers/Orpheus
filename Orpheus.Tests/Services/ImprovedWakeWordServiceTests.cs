using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Orpheus.Configuration;
using Orpheus.Services.WakeWord;
using Pv;
using Xunit;

namespace Orpheus.Tests.Services;

public class ImprovedWakeWordServiceTests
{
    [Fact]
    public void WakeWordConfiguration_ParsesBuiltInKeywords_Correctly()
    {
        // Arrange
        var configData = new Dictionary<string, string?>
        {
            ["WakeWord:EnabledWords"] = "computer,jarvis",
            ["WakeWord:Sensitivities:computer"] = "0.6",
            ["WakeWord:Sensitivities:jarvis"] = "0.7"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var wakeWordConfig = new WakeWordConfiguration(configuration);

        // Act & Assert
        Assert.True(wakeWordConfig.IsBuiltInKeyword("computer"));
        Assert.True(wakeWordConfig.IsBuiltInKeyword("jarvis"));
        Assert.False(wakeWordConfig.IsBuiltInKeyword("orpheus"));
        
        Assert.Equal(BuiltInKeyword.COMPUTER, wakeWordConfig.TryParseBuiltInKeyword("computer"));
        Assert.Equal(BuiltInKeyword.JARVIS, wakeWordConfig.TryParseBuiltInKeyword("jarvis"));
        Assert.Null(wakeWordConfig.TryParseBuiltInKeyword("orpheus"));

        Assert.Equal(0.6f, wakeWordConfig.GetSensitivity("computer"));
        Assert.Equal(0.7f, wakeWordConfig.GetSensitivity("jarvis"));
    }

    [Fact]
    public void WakeWordConfiguration_EnabledWords_ReturnsConfiguredWords()
    {
        // Arrange
        var configData = new Dictionary<string, string?>
        {
            ["WakeWord:EnabledWords"] = "computer,orpheus,jarvis"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var wakeWordConfig = new WakeWordConfiguration(configuration);

        // Act
        var enabledWords = wakeWordConfig.EnabledWakeWords;

        // Assert
        Assert.Equal(3, enabledWords.Length);
        Assert.Contains("computer", enabledWords);
        Assert.Contains("orpheus", enabledWords);
        Assert.Contains("jarvis", enabledWords);
    }

    [Fact]
    public void WakeWordConfiguration_DefaultValues_AreCorrect()
    {
        // Arrange - Empty configuration
        var configuration = new ConfigurationBuilder().Build();
        var wakeWordConfig = new WakeWordConfiguration(configuration);

        // Act & Assert
        var defaultWords = wakeWordConfig.EnabledWakeWords;
        Assert.Equal(2, defaultWords.Length);
        Assert.Contains("computer", defaultWords);
        Assert.Contains("orpheus", defaultWords);

        // Check default sensitivities
        Assert.Equal(0.6f, wakeWordConfig.GetSensitivity("computer"));
        Assert.Equal(0.7f, wakeWordConfig.GetSensitivity("orpheus"));
        Assert.Equal(0.6f, wakeWordConfig.GetSensitivity("jarvis"));
        
        // Check default cooldown
        Assert.Equal(3000, wakeWordConfig.DetectionCooldownMs);
    }

    [Fact]
    public void ImprovedWakeWordService_Constructor_DoesNotThrow()
    {
        // Arrange
        var logger = Mock.Of<ILogger<ImprovedPicovoiceWakeWordService>>();
        var configuration = Mock.Of<IConfiguration>();

        // Act & Assert
        var service = new ImprovedPicovoiceWakeWordService(logger, configuration);
        Assert.NotNull(service);
        Assert.False(service.IsInitialized);
    }

    [Fact]
    public void WakeWordConfiguration_GetCustomModelPath_ReturnsCorrectPath()
    {
        // Arrange
        var configData = new Dictionary<string, string?>
        {
            ["WakeWord:CustomModels:orpheus"] = "Resources/orpheus_keyword_file.ppn",
            ["WakeWord:CustomModels:custom"] = "Resources/custom_wake_word.ppn"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var wakeWordConfig = new WakeWordConfiguration(configuration);

        // Act & Assert
        Assert.Equal("Resources/orpheus_keyword_file.ppn", wakeWordConfig.GetCustomModelPath("orpheus"));
        Assert.Equal("Resources/custom_wake_word.ppn", wakeWordConfig.GetCustomModelPath("custom"));
        
        // Test default path for orpheus when not configured
        var emptyConfig = new WakeWordConfiguration(new ConfigurationBuilder().Build());
        Assert.Equal("Resources/orpheus_keyword_file.ppn", emptyConfig.GetCustomModelPath("orpheus"));
    }
}