using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;

namespace Orpheus.Services;

public interface IMessageUpdateService
{
    Task RegisterInteractionForSongUpdatesAsync(ulong interactionId, ApplicationCommandInteraction interaction, string songId, string originalMessage, bool isDeferred = false);
    Task SendSongTitleUpdateAsync(string songId, string actualTitle);
    void RemoveInteraction(ulong interactionId);
}

public class MessageUpdateService : IMessageUpdateService
{
    private readonly ILogger<MessageUpdateService> _logger;
    private readonly Dictionary<string, List<InteractionContext>> _songInteractionMap = new();
    private readonly object _lock = new();

    public MessageUpdateService(ILogger<MessageUpdateService> logger)
    {
        _logger = logger;
    }

    public async Task RegisterInteractionForSongUpdatesAsync(ulong interactionId, ApplicationCommandInteraction interaction, string songId, string originalMessage, bool isDeferred = false)
    {
        var context = new InteractionContext(interactionId, interaction, originalMessage, isDeferred);
        
        lock (_lock)
        {
            if (!_songInteractionMap.ContainsKey(songId))
            {
                _songInteractionMap[songId] = new List<InteractionContext>();
            }
            
            _songInteractionMap[songId].Add(context);
        }

        _logger.LogDebug("Registered interaction {InteractionId} for song updates: {SongId}, deferred: {IsDeferred}", interactionId, songId, isDeferred);
        await Task.CompletedTask;
    }

    public async Task SendSongTitleUpdateAsync(string songId, string actualTitle)
    {
        _logger.LogDebug("SendSongTitleUpdateAsync called for songId: {SongId}, title: '{Title}'", songId, actualTitle);
        
        List<InteractionContext>? interactions;
        
        lock (_lock)
        {
            if (!_songInteractionMap.TryGetValue(songId, out interactions) || interactions == null)
            {
                _logger.LogWarning("No interactions found for song ID: {SongId}", songId);
                return; // No interactions waiting for this song
            }
            
            _logger.LogDebug("Found {Count} interactions for song ID: {SongId}", interactions.Count, songId);
            
            // Remove the song from the map since we're sending the update
            _songInteractionMap.Remove(songId);
        }

        foreach (var context in interactions)
        {
            try
            {
                // Update the message content with actual title
                var originalContent = context.OriginalMessage ?? string.Empty;
                _logger.LogDebug("Original message content: '{Content}'", originalContent);
                
                var updatedContent = originalContent;
                
                // Replace placeholder text with actual title
                if (originalContent.Contains("YouTube Video"))
                {
                    updatedContent = originalContent.Replace("YouTube Video", actualTitle);
                    _logger.LogDebug("Replaced 'YouTube Video' with '{Title}'", actualTitle);
                }
                else if (originalContent.Contains("Found: "))
                {
                    // For search queries, replace the search term with actual title
                    var foundIndex = originalContent.IndexOf("Found: ");
                    if (foundIndex >= 0)
                    {
                        var beforeFound = originalContent.Substring(0, foundIndex);
                        var afterFound = originalContent.Substring(foundIndex + 7); // Skip "Found: "
                        
                        _logger.LogDebug("Before 'Found: ': '{Before}', After 'Found: ': '{After}'", beforeFound, afterFound);
                        
                        // Find the end of the query (before "** to queue")
                        var endIndex = afterFound.IndexOf("** to queue");
                        if (endIndex >= 0)
                        {
                            var afterTitle = afterFound.Substring(endIndex);
                            updatedContent = beforeFound + "**" + actualTitle + afterTitle;
                            _logger.LogDebug("Found '** to queue' pattern, updated content: '{Content}'", updatedContent);
                        }
                        else
                        {
                            // Fallback - replace everything after "Found: " up to first "**"
                            var starIndex = afterFound.IndexOf("**");
                            if (starIndex >= 0)
                            {
                                var afterTitle = afterFound.Substring(starIndex);
                                updatedContent = beforeFound + "**" + actualTitle + afterTitle;
                                _logger.LogDebug("Found '**' pattern, updated content: '{Content}'", updatedContent);
                            }
                            else
                            {
                                // Last resort fallback
                                updatedContent = beforeFound + "**" + actualTitle + "** to queue and starting playback!";
                                _logger.LogDebug("Used fallback pattern, updated content: '{Content}'", updatedContent);
                            }
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("Message content doesn't contain expected placeholders. Original: '{Content}'", originalContent);
                }
                
                // Update the original response
                _logger.LogDebug("Attempting to modify Discord response...");
                await context.Interaction.ModifyResponseAsync(properties =>
                {
                    properties.Content = updatedContent;
                });
                
                _logger.LogInformation("Successfully updated Discord message with real song title: '{Title}'", actualTitle);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update message for interaction {InteractionId}: {Error}", context.InteractionId, ex.Message);
            }
        }
    }

    public void RemoveInteraction(ulong interactionId)
    {
        lock (_lock)
        {
            foreach (var kvp in _songInteractionMap.ToList())
            {
                kvp.Value.RemoveAll(ctx => ctx.InteractionId == interactionId);
                if (kvp.Value.Count == 0)
                {
                    _songInteractionMap.Remove(kvp.Key);
                }
            }
        }
    }

    private record InteractionContext(ulong InteractionId, ApplicationCommandInteraction Interaction, string? OriginalMessage, bool IsDeferred = false);
}