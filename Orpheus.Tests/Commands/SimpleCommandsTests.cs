using Orpheus.Commands;

namespace Orpheus.Tests.Commands;

public class SimpleCommandsTests
{
    [Fact]
    public void PingCommand_ReturnsExpectedResponse()
    {
        // Act
        var result = Ping.Command();

        // Assert
        Assert.Equal("Pong!", result);
    }

    [Fact]
    public void SquareCommand_WithPositiveNumber_ReturnsCorrectSquare()
    {
        // Arrange
        const int number = 5;

        // Act
        var result = Square.Command(number);

        // Assert
        Assert.Equal("5² = 25", result);
    }

    [Fact]
    public void SquareCommand_WithZero_ReturnsZero()
    {
        // Act
        var result = Square.Command(0);

        // Assert
        Assert.Equal("0² = 0", result);
    }

    [Fact]
    public void SquareCommand_WithNegativeNumber_ReturnsCorrectSquare()
    {
        // Arrange
        const int number = -3;

        // Act
        var result = Square.Command(number);

        // Assert
        Assert.Equal("-3² = 9", result);
    }

    [Theory]
    [InlineData(1, "1² = 1")]
    [InlineData(2, "2² = 4")]
    [InlineData(10, "10² = 100")]
    [InlineData(-5, "-5² = 25")]
    [InlineData(100, "100² = 10000")]
    public void SquareCommand_WithVariousNumbers_ReturnsCorrectFormat(int input, string expected)
    {
        // Act
        var result = Square.Command(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void SquareCommand_WithMaxValue_HandlesLargeNumbers()
    {
        // Arrange
        const int number = 1000;
        const string expected = "1000² = 1000000";

        // Act
        var result = Square.Command(number);

        // Assert
        Assert.Equal(expected, result);
    }
}