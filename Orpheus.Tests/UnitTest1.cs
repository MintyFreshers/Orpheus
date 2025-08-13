using Orpheus.Services.Queue;

namespace Orpheus.Tests.Queue;

public class QueuedSongTests
{
    [Fact]
    public void Constructor_SetsAllProperties_Correctly()
    {
        // Arrange
        const string title = "Test Song";
        const string url = "https://example.com/song";
        const ulong userId = 12345UL;
        const string filePath = "/path/to/file.mp3";
        
        // Act
        var queuedSong = new QueuedSong(title, url, userId, filePath);
        
        // Assert
        Assert.Equal(title, queuedSong.Title);
        Assert.Equal(url, queuedSong.Url);
        Assert.Equal(userId, queuedSong.RequestedByUserId);
        Assert.Equal(filePath, queuedSong.FilePath);
        Assert.NotNull(queuedSong.Id);
        Assert.NotEqual(string.Empty, queuedSong.Id);
        Assert.True(queuedSong.QueuedAt <= DateTimeOffset.UtcNow);
        Assert.True(queuedSong.QueuedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public void Constructor_WithoutFilePath_SetsFilePathToNull()
    {
        // Arrange & Act
        var queuedSong = new QueuedSong("Test Song", "https://example.com/song", 12345UL);
        
        // Assert
        Assert.Null(queuedSong.FilePath);
    }

    [Fact]
    public void Id_IsUniqueForEachInstance()
    {
        // Arrange & Act
        var song1 = new QueuedSong("Song 1", "https://example.com/1", 1UL);
        var song2 = new QueuedSong("Song 2", "https://example.com/2", 2UL);
        
        // Assert
        Assert.NotEqual(song1.Id, song2.Id);
    }

    [Fact]
    public void Title_CanBeModified()
    {
        // Arrange
        var queuedSong = new QueuedSong("Original Title", "https://example.com/song", 12345UL);
        const string newTitle = "Updated Title";
        
        // Act
        queuedSong.Title = newTitle;
        
        // Assert
        Assert.Equal(newTitle, queuedSong.Title);
    }

    [Fact]
    public void FilePath_CanBeModified()
    {
        // Arrange
        var queuedSong = new QueuedSong("Test Song", "https://example.com/song", 12345UL);
        const string newFilePath = "/new/path/to/file.mp3";
        
        // Act
        queuedSong.FilePath = newFilePath;
        
        // Assert
        Assert.Equal(newFilePath, queuedSong.FilePath);
    }
}