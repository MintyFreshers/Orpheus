using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Gateway;

namespace Orpheus.Services;

public interface IMessageUpdateService
{
    Task RegisterInteractionForSongUpdatesAsync(ulong interactionId, ApplicationCommandInteraction interaction, string songId, string originalMessage, bool isDeferred = false);
    Task RegisterMessageForSongUpdatesAsync(ulong messageId, ulong channelId, GatewayClient client, string songId, string originalMessage);
    Task SendSongTitleUpdateAsync(string songId, string actualTitle);
    void RemoveInteraction(ulong interactionId);
}

public class MessageUpdateService : IMessageUpdateService
{
    private readonly ILogger<MessageUpdateService> _logger;
    private readonly Dictionary<string, List<InteractionContext>> _songInteractionMap = new();
    private readonly Dictionary<string, List<MessageContext>> _songMessageMap = new();
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

    public async Task RegisterMessageForSongUpdatesAsync(ulong messageId, ulong channelId, GatewayClient client, string songId, string originalMessage)
    {
        var context = new MessageContext(messageId, channelId, client, originalMessage);
        
        lock (_lock)
        {
            if (!_songMessageMap.ContainsKey(songId))
            {
                _songMessageMap[songId] = new List<MessageContext>();
            }
            
            _songMessageMap[songId].Add(context);
        }

        _logger.LogDebug("Registered message {MessageId} in channel {ChannelId} for song updates: {SongId}", messageId, channelId, songId);
        await Task.CompletedTask;
    }

    public async Task SendSongTitleUpdateAsync(string songId, string actualTitle)
    {
        _logger.LogDebug("SendSongTitleUpdateAsync called for songId: {SongId}, title: '{Title}'", songId, actualTitle);
        
        List<InteractionContext>? interactions;
        List<MessageContext>? messages;
        
        lock (_lock)
        {
            _songInteractionMap.TryGetValue(songId, out interactions);
            _songMessageMap.TryGetValue(songId, out messages);
            
            if ((interactions == null || interactions.Count == 0) && (messages == null || messages.Count == 0))
            {
                _logger.LogWarning("No interactions or messages found for song ID: {SongId}", songId);
                return; // No interactions or messages waiting for this song
            }
            
            _logger.LogDebug("Found {InteractionCount} interactions and {MessageCount} messages for song ID: {SongId}", 
                interactions?.Count ?? 0, messages?.Count ?? 0, songId);
            
            // Remove the song from both maps since we're sending the update
            _songInteractionMap.Remove(songId);
            _songMessageMap.Remove(songId);
        }

        // Update interaction responses
        if (interactions != null)
        {
            foreach (var context in interactions)
            {
                try
                {
                    var updatedContent = UpdateMessageContent(context.OriginalMessage ?? string.Empty, actualTitle);
                    
                    // Update the original response
                    _logger.LogDebug("Attempting to modify Discord interaction response...");
                    await context.Interaction.ModifyResponseAsync(properties =>
                    {
                        properties.Content = updatedContent;
                    });
                    
                    _logger.LogInformation("Successfully updated Discord interaction message with real song title: '{Title}'", actualTitle);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update interaction message for interaction {InteractionId}: {Error}", context.InteractionId, ex.Message);
                }
            }
        }

        // Update regular messages
        if (messages != null)
        {
            foreach (var context in messages)
            {
                try
                {
                    var updatedContent = UpdateMessageContent(context.OriginalMessage ?? string.Empty, actualTitle);
                    
                    // Update the regular message
                    _logger.LogDebug("Attempting to modify Discord message {MessageId}...", context.MessageId);
                    await context.Client.Rest.ModifyMessageAsync(context.ChannelId, context.MessageId, options =>
                    {
                        options.Content = updatedContent;
                    });
                    
                    _logger.LogInformation("Successfully updated Discord message {MessageId} with real song title: '{Title}'", context.MessageId, actualTitle);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update message {MessageId} in channel {ChannelId}: {Error}", context.MessageId, context.ChannelId, ex.Message);
                }
            }
        }
    }

    private string UpdateMessageContent(string originalContent, string actualTitle)
    {
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
        
        return updatedContent;
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
    private record MessageContext(ulong MessageId, ulong ChannelId, GatewayClient Client, string? OriginalMessage);
}