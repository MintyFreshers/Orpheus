using Microsoft.Extensions.Logging;
using Moq;
using Orpheus.Services.Queue;

namespace Orpheus.Tests.Queue;

public class SongQueueServiceTests
{
    private readonly Mock<ILogger<SongQueueService>> _mockLogger;
    private readonly SongQueueService _service;

    public SongQueueServiceTests()
    {
        _mockLogger = new Mock<ILogger<SongQueueService>>();
        _service = new SongQueueService(_mockLogger.Object);
    }

    [Fact]
    public void InitialState_IsEmpty()
    {
        // Assert
        Assert.Equal(0, _service.Count);
        Assert.True(_service.IsEmpty);
        Assert.Null(_service.CurrentSong);
        Assert.Null(_service.PeekNext());
        Assert.Empty(_service.GetQueue());
    }

    [Fact]
    public void EnqueueSong_AddsSongToQueue_AndFiresEvent()
    {
        // Arrange
        var song = new QueuedSong("Test Song", "https://example.com/song", 12345UL);
        QueuedSong? eventSong = null;
        _service.SongAdded += (s) => eventSong = s;

        // Act
        _service.EnqueueSong(song);

        // Assert
        Assert.Equal(1, _service.Count);
        Assert.False(_service.IsEmpty);
        Assert.Equal(song, _service.PeekNext());
        Assert.Contains(song, _service.GetQueue());
        Assert.Equal(song, eventSong);
    }

    [Fact]
    public void EnqueueSongNext_AddsSongToFrontOfQueue()
    {
        // Arrange
        var firstSong = new QueuedSong("First Song", "https://example.com/1", 1UL);
        var secondSong = new QueuedSong("Second Song", "https://example.com/2", 2UL);
        var nextSong = new QueuedSong("Next Song", "https://example.com/next", 3UL);
        
        _service.EnqueueSong(firstSong);
        _service.EnqueueSong(secondSong);

        // Act
        _service.EnqueueSongNext(nextSong);

        // Assert
        Assert.Equal(3, _service.Count);
        Assert.Equal(nextSong, _service.PeekNext());
        
        var queue = _service.GetQueue();
        Assert.Equal(nextSong, queue[0]);
        Assert.Equal(firstSong, queue[1]);
        Assert.Equal(secondSong, queue[2]);
    }

    [Fact]
    public void DequeueNext_ReturnsAndRemovesSong()
    {
        // Arrange
        var song1 = new QueuedSong("Song 1", "https://example.com/1", 1UL);
        var song2 = new QueuedSong("Song 2", "https://example.com/2", 2UL);
        _service.EnqueueSong(song1);
        _service.EnqueueSong(song2);

        // Act
        var dequeuedSong = _service.DequeueNext();

        // Assert
        Assert.Equal(song1, dequeuedSong);
        Assert.Equal(1, _service.Count);
        Assert.Equal(song2, _service.PeekNext());
    }

    [Fact]
    public void DequeueNext_WhenEmpty_ReturnsNull()
    {
        // Act
        var result = _service.DequeueNext();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void PeekNext_DoesNotRemoveSong()
    {
        // Arrange
        var song = new QueuedSong("Test Song", "https://example.com/song", 12345UL);
        _service.EnqueueSong(song);

        // Act
        var peekedSong1 = _service.PeekNext();
        var peekedSong2 = _service.PeekNext();

        // Assert
        Assert.Equal(song, peekedSong1);
        Assert.Equal(song, peekedSong2);
        Assert.Equal(1, _service.Count);
    }

    [Fact]
    public void ClearQueue_RemovesAllSongs()
    {
        // Arrange
        _service.EnqueueSong(new QueuedSong("Song 1", "https://example.com/1", 1UL));
        _service.EnqueueSong(new QueuedSong("Song 2", "https://example.com/2", 2UL));
        _service.SetCurrentSong(new QueuedSong("Current", "https://example.com/current", 0UL));

        // Act
        _service.ClearQueue();

        // Assert
        Assert.Equal(0, _service.Count);
        Assert.True(_service.IsEmpty);
        Assert.Empty(_service.GetQueue());
        // Current song should remain unchanged
        Assert.NotNull(_service.CurrentSong);
    }

    [Fact]
    public void SetCurrentSong_UpdatesCurrentSong()
    {
        // Arrange
        var song = new QueuedSong("Current Song", "https://example.com/current", 12345UL);

        // Act
        _service.SetCurrentSong(song);

        // Assert
        Assert.Equal(song, _service.CurrentSong);
    }

    [Fact]
    public void SetCurrentSong_CanSetToNull()
    {
        // Arrange
        var song = new QueuedSong("Current Song", "https://example.com/current", 12345UL);
        _service.SetCurrentSong(song);

        // Act
        _service.SetCurrentSong(null);

        // Assert
        Assert.Null(_service.CurrentSong);
    }

    [Fact]
    public void GetQueue_ReturnsReadOnlyView()
    {
        // Arrange
        var song1 = new QueuedSong("Song 1", "https://example.com/1", 1UL);
        var song2 = new QueuedSong("Song 2", "https://example.com/2", 2UL);
        _service.EnqueueSong(song1);
        _service.EnqueueSong(song2);

        // Act
        var queue = _service.GetQueue();

        // Assert
        Assert.Equal(2, queue.Count);
        Assert.Equal(song1, queue[0]);
        Assert.Equal(song2, queue[1]);
        Assert.IsAssignableFrom<IReadOnlyList<QueuedSong>>(queue);
    }

    [Fact]
    public void SongAdded_Event_IsTriggeredForBothEnqueueMethods()
    {
        // Arrange
        var regularSong = new QueuedSong("Regular Song", "https://example.com/regular", 1UL);
        var nextSong = new QueuedSong("Next Song", "https://example.com/next", 2UL);
        var eventSongs = new List<QueuedSong>();
        _service.SongAdded += eventSongs.Add;

        // Act
        _service.EnqueueSong(regularSong);
        _service.EnqueueSongNext(nextSong);

        // Assert
        Assert.Equal(2, eventSongs.Count);
        Assert.Contains(regularSong, eventSongs);
        Assert.Contains(nextSong, eventSongs);
    }
}