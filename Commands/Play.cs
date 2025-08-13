using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Orpheus.Services;
using Orpheus.Services.Queue;
using Orpheus.Services.Downloader.Youtube;

namespace Orpheus.Commands;

public class Play : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly ISongQueueService _queueService;
    private readonly IQueuePlaybackService _queuePlaybackService;
    private readonly IYouTubeDownloader _downloader;
    private readonly IMessageUpdateService _messageUpdateService;
    private readonly ILogger<Play> _logger;

    public Play(
        ISongQueueService queueService,
        IQueuePlaybackService queuePlaybackService,
        IYouTubeDownloader downloader,
        IMessageUpdateService messageUpdateService,
        ILogger<Play> logger)
    {
        _queueService = queueService;
        _queuePlaybackService = queuePlaybackService;
        _downloader = downloader;
        _messageUpdateService = messageUpdateService;
        _logger = logger;
    }

    [SlashCommand("play", "Add a YouTube video to the queue by URL or search query.", Contexts = [InteractionContextType.Guild])]
    public async Task Command(string query)
    {
        var guild = Context.Guild!;
        var client = Context.Client;
        var userId = Context.User.Id;

        _logger.LogInformation("Received /play command for query: {Query} from user {UserId} in guild {GuildId}", query, userId, guild.Id);

        try
        {
            string? url = null;
            string placeholderTitle;
            bool isSearchQuery = false;
            
            // Check if the input is a URL or a search query
            if (IsUrl(query))
            {
                url = query;
                placeholderTitle = GetPlaceholderTitle(url);
                _logger.LogDebug("Input detected as URL: {Url}", url);
            }
            else
            {
                // It's a search query - respond immediately to avoid timeout
                isSearchQuery = true;
                await RespondAsync(InteractionCallback.Message($"🔍 Searching for: **{query}**..."));
                
                _logger.LogDebug("Input detected as search query: {Query}", query);
                
                // Use the optimized search method that returns both URL and metadata
                var searchResult = await _downloader.SearchAndGetFirstResultAsync(query);
                if (searchResult == null)
                {
                    await Context.Interaction.ModifyResponseAsync(properties => 
                        properties.Content = $"❌ No results found for: **{query}**");
                    return;
                }
                
                url = searchResult.Url;
                // Use the title from search results if available, otherwise use placeholder
                placeholderTitle = !string.IsNullOrWhiteSpace(searchResult.Title) ? searchResult.Title : $"Found: {query}";
                
                _logger.LogInformation("Optimized search found URL: {Url} with title: {Title} for query: {Query}", 
                    url, placeholderTitle, query);
            }
            
            // Check if queue was empty before adding
            var wasQueueEmpty = _queueService.IsEmpty && _queueService.CurrentSong == null;
            
            // Create queued song immediately with placeholder title
            var queuedSong = new QueuedSong(placeholderTitle, url, userId);
            _queueService.EnqueueSong(queuedSong);

            var queuePosition = _queueService.Count;
            var message = wasQueueEmpty
                ? $"✅ Added **{placeholderTitle}** to queue and starting playback!" 
                : $"✅ Added **{placeholderTitle}** to queue (position {queuePosition})";

            // Respond differently based on whether this was a search query or direct URL
            if (isSearchQuery)
            {
                await Context.Interaction.ModifyResponseAsync(properties => properties.Content = message);
            }
            else
            {
                await RespondAsync(InteractionCallback.Message(message));
            }

            // Register for message updates when real title is fetched
            await _messageUpdateService.RegisterInteractionForSongUpdatesAsync(Context.Interaction.Id, Context.Interaction, queuedSong.Id, message, isSearchQuery);

            // Auto-start queue processing if queue was empty (first song added)
            if (wasQueueEmpty || !_queuePlaybackService.IsProcessing)
            {
                await _queuePlaybackService.StartQueueProcessingAsync(guild, client, userId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in /play command for query: {Query}", query);
            
            // Try to respond appropriately based on interaction state
            try
            {
                if (Context.Interaction.Token != null) // Simple check if interaction is still valid
                {
                    await Context.Interaction.ModifyResponseAsync(properties => 
                        properties.Content = "❌ An error occurred while adding the song to the queue.");
                }
                else
                {
                    await RespondAsync(InteractionCallback.Message("❌ An error occurred while adding the song to the queue."));
                }
            }
            catch (Exception responseEx)
            {
                _logger.LogError(responseEx, "Failed to send error response for query: {Query}", query);
            }
        }
    }

    private static bool IsUrl(string input)
    {
        return Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string GetPlaceholderTitle(string url)
    {
        // Return immediate placeholder based on URL type - no async calls to avoid timeout
        if (url.Contains("youtube.com") || url.Contains("youtu.be"))
        {
            return "YouTube Video"; // Will be updated by background service
        }
        return "Audio Track";
    }
}