using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Orpheus.Configuration;
using Orpheus.Services.WakeWord;
using Xunit;

namespace Orpheus.Tests.Services;

public class WakeWordIntegrationTests
{
    [Fact]
    public void WakeWordService_CanBeCreated_WithDependencyInjection()
    {
        // Arrange
        var services = new ServiceCollection();
        
        // Mock configuration with wake word settings
        var configData = new Dictionary<string, string?>
        {
            ["WakeWord:EnabledWords"] = "computer,jarvis",
            ["WakeWord:DetectionCooldownMs"] = "2500",
            ["WakeWord:Sensitivities:computer"] = "0.6",
            ["WakeWord:Sensitivities:jarvis"] = "0.7"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // Register services like in Program.cs
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<ILogger<ImprovedPicovoiceWakeWordService>>(
            Mock.Of<ILogger<ImprovedPicovoiceWakeWordService>>());
        services.AddSingleton<WakeWordConfiguration>();
        services.AddSingleton<IWakeWordDetectionService, ImprovedPicovoiceWakeWordService>();

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var wakeWordService = serviceProvider.GetRequiredService<IWakeWordDetectionService>();
        var wakeWordConfig = serviceProvider.GetRequiredService<WakeWordConfiguration>();

        // Assert
        Assert.NotNull(wakeWordService);
        Assert.IsType<ImprovedPicovoiceWakeWordService>(wakeWordService);
        Assert.False(wakeWordService.IsInitialized); // Should not be initialized without Picovoice key
        
        Assert.NotNull(wakeWordConfig);
        Assert.Equal(2, wakeWordConfig.EnabledWakeWords.Length);
        Assert.Contains("computer", wakeWordConfig.EnabledWakeWords);
        Assert.Contains("jarvis", wakeWordConfig.EnabledWakeWords);
        Assert.Equal(2500, wakeWordConfig.DetectionCooldownMs);
        Assert.Equal(0.6f, wakeWordConfig.GetSensitivity("computer"));
        Assert.Equal(0.7f, wakeWordConfig.GetSensitivity("jarvis"));
    }

    [Fact]
    public void WakeWordConfiguration_LoadsFromConfiguration_Correctly()
    {
        // Arrange - Configuration similar to real appsettings.json
        var configData = new Dictionary<string, string?>
        {
            ["WakeWord:EnabledWords"] = "computer,orpheus",
            ["WakeWord:DetectionCooldownMs"] = "3000", 
            ["WakeWord:Sensitivities:computer"] = "0.6",
            ["WakeWord:Sensitivities:orpheus"] = "0.7",
            ["WakeWord:CustomModels:orpheus"] = "Resources/orpheus_keyword_file.ppn",
            ["PicovoiceAccessKey"] = "fake-key-for-testing"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // Act
        var wakeWordConfig = new WakeWordConfiguration(configuration);

        // Assert - Verify configuration matches what we'd expect from appsettings.json
        var enabledWords = wakeWordConfig.EnabledWakeWords;
        Assert.Equal(2, enabledWords.Length);
        Assert.Contains("computer", enabledWords);
        Assert.Contains("orpheus", enabledWords);
        
        // Check that built-in vs custom keywords are properly identified
        Assert.True(wakeWordConfig.IsBuiltInKeyword("computer"));
        Assert.False(wakeWordConfig.IsBuiltInKeyword("orpheus"));
        
        // Check custom model path
        Assert.Equal("Resources/orpheus_keyword_file.ppn", wakeWordConfig.GetCustomModelPath("orpheus"));
        
        // Check cooldown
        Assert.Equal(3000, wakeWordConfig.DetectionCooldownMs);
        
        // Check sensitivities
        Assert.Equal(0.6f, wakeWordConfig.GetSensitivity("computer"));
        Assert.Equal(0.7f, wakeWordConfig.GetSensitivity("orpheus"));
    }
    
    [Fact]
    public void ImprovedWakeWordService_Initialize_FailsGracefullyWithoutAccessKey()
    {
        // Arrange
        var configData = new Dictionary<string, string?>
        {
            ["WakeWord:EnabledWords"] = "computer"
            // Intentionally omit PicovoiceAccessKey to test error handling
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var logger = Mock.Of<ILogger<ImprovedPicovoiceWakeWordService>>();
        var service = new ImprovedPicovoiceWakeWordService(logger, configuration);

        // Act
        service.Initialize(); // Should not throw, should handle missing key gracefully

        // Assert
        Assert.False(service.IsInitialized); // Should remain false due to missing access key
    }

    [Theory]
    [InlineData("computer", "COMPUTER")]
    [InlineData("jarvis", "JARVIS")]
    [InlineData("bumblebee", "BUMBLEBEE")]
    [InlineData("hey google", "HEY_GOOGLE")]
    [InlineData("ok google", "OK_GOOGLE")]
    [InlineData("orpheus", null)]
    [InlineData("custom", null)]
    public void WakeWordConfiguration_BuiltInKeywordMapping_WorksCorrectly(string input, string? expected)
    {
        // Arrange
        var configuration = new ConfigurationBuilder().Build();
        var wakeWordConfig = new WakeWordConfiguration(configuration);

        // Act
        var result = wakeWordConfig.TryParseBuiltInKeyword(input);

        // Assert
        if (expected == null)
        {
            Assert.Null(result);
            Assert.False(wakeWordConfig.IsBuiltInKeyword(input));
        }
        else
        {
            Assert.NotNull(result);
            Assert.Equal(expected, result.ToString());
            Assert.True(wakeWordConfig.IsBuiltInKeyword(input));
        }
    }
}