using Microsoft.Extensions.Logging;
using NetCord.Gateway;
using Orpheus.Configuration;
using Orpheus.Services.VoiceClientController;
using Orpheus.Services.Queue;
using Orpheus.Services.Downloader.Youtube;
using Orpheus.Services;
using System.Linq;

namespace Orpheus.Services.Transcription;

public class VoiceCommandProcessor : IVoiceCommandProcessor
{
    private readonly ILogger<VoiceCommandProcessor> _logger;
    private readonly IVoiceClientController _voiceClientController;
    private readonly ISongQueueService _queueService;
    private readonly IQueuePlaybackService _queuePlaybackService;
    private readonly IYouTubeDownloader _downloader;
    private readonly IMessageUpdateService _messageUpdateService;
    private readonly BotConfiguration _botConfiguration;

    public VoiceCommandProcessor(
        ILogger<VoiceCommandProcessor> logger,
        IVoiceClientController voiceClientController,
        ISongQueueService queueService,
        IQueuePlaybackService queuePlaybackService,
        IYouTubeDownloader downloader,
        IMessageUpdateService messageUpdateService,
        BotConfiguration botConfiguration)
    {
        _logger = logger;
        _voiceClientController = voiceClientController;
        _queueService = queueService;
        _queuePlaybackService = queuePlaybackService;
        _downloader = downloader;
        _messageUpdateService = messageUpdateService;
        _botConfiguration = botConfiguration;
    }

    public Task<string> ProcessCommandAsync(string transcription, ulong userId)
    {
        // Legacy method - call the enhanced version without client context
        return ProcessBasicCommandAsync(transcription, userId);
    }

    public async Task<string> ProcessCommandAsync(string transcription, ulong userId, GatewayClient client)
    {
        if (string.IsNullOrWhiteSpace(transcription))
        {
            _logger.LogWarning("Received empty transcription from user {UserId}", userId);
            return "I didn't hear anything clearly.";
        }

        var normalizedCommand = transcription.ToLowerInvariant().Trim();
        _logger.LogInformation("Processing voice command: '{Command}' from user {UserId}", normalizedCommand, userId);

        // Get guild using configured guild ID for single-guild bot
        Guild? guild = null;
        try
        {
            var guildId = _botConfiguration.DefaultGuildId;
            // Try to get guild from cache
            guild = client.Cache.Guilds.GetValueOrDefault(guildId);
            
            if (guild == null)
            {
                _logger.LogWarning("Guild {GuildId} not found in cache, voice command may fail", guildId);
                return CreateUserMentionResponse(userId, "I couldn't find the server in cache. Make sure the bot is connected properly.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get guild context for voice command");
            return CreateUserMentionResponse(userId, "I couldn't determine the server context for this command.");
        }

        if (guild == null)
        {
            _logger.LogError("Could not determine guild for voice command");
            return CreateUserMentionResponse(userId, "I couldn't determine the server for this command.");
        }

        // Parse and execute commands
        try
        {
            return await ProcessParsedCommandAsync(normalizedCommand, userId, guild, client);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing voice command: {Command}", normalizedCommand);
            return CreateUserMentionResponse(userId, "An error occurred while executing the command.");
        }
    }

    private async Task<string> ProcessParsedCommandAsync(string normalizedCommand, ulong userId, Guild guild, GatewayClient client)
    {
        // Handle "leave" command
        if (normalizedCommand.Equals("leave") || normalizedCommand.Contains("leave voice") || normalizedCommand.Contains("disconnect"))
        {
            _logger.LogInformation("Recognized leave command from user {UserId}", userId);
            var result = await _voiceClientController.LeaveVoiceChannelAsync(guild, client);
            return CreateUserMentionResponse(userId, result);
        }

        // Handle "playtest" command  
        if (normalizedCommand.Equals("playtest") || normalizedCommand.Contains("play test"))
        {
            _logger.LogInformation("Recognized playtest command from user {UserId}", userId);
            const string testFilePath = "Resources/ExampleTrack.mp3";
            
            if (!File.Exists(testFilePath))
            {
                return CreateUserMentionResponse(userId, $"Test file not found: {testFilePath}");
            }
            
            var result = await _voiceClientController.PlayMp3Async(guild, client, userId, testFilePath);
            return CreateUserMentionResponse(userId, result);
        }

        // Handle "play <song>" command
        if (normalizedCommand.StartsWith("play ") && normalizedCommand.Length > 5)
        {
            var songQuery = normalizedCommand.Substring(5).Trim();
            _logger.LogInformation("Recognized play command from user {UserId} with query: {Query}", userId, songQuery);
            
            try
            {
                return await ProcessPlayCommandAsync(songQuery, userId, guild, client);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing play command for query: {Query}", songQuery);
                return CreateUserMentionResponse(userId, "Failed to add the song to the queue.");
            }
        }

        // Fall back to basic command processing for backwards compatibility
        return await ProcessBasicCommandAsync(normalizedCommand, userId);
    }

    private async Task<string> ProcessPlayCommandAsync(string query, ulong userId, Guild guild, GatewayClient client)
    {
        string? url;
        string placeholderTitle;

        // Check if the input is a URL or a search query
        if (IsUrl(query))
        {
            url = query;
            placeholderTitle = GetPlaceholderTitle(url);
            _logger.LogDebug("Voice play input detected as URL: {Url}", url);
        }
        else
        {
            // It's a search query
            _logger.LogDebug("Voice play input detected as search query: {Query}", query);
            
            // Search for the first result
            url = await _downloader.SearchAndGetFirstUrlAsync(query);
            if (url == null)
            {
                return CreateUserMentionResponse(userId, $"❌ No results found for: **{query}**");
            }
            
            _logger.LogInformation("Voice search found URL: {Url} for query: {Query}", url, query);
            placeholderTitle = $"Found: {query}"; // Will be updated with real title
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

        // Auto-start queue processing if queue was empty (first song added)
        if (wasQueueEmpty || !_queuePlaybackService.IsProcessing)
        {
            await _queuePlaybackService.StartQueueProcessingAsync(guild, client, userId);
        }

        return CreateUserMentionResponse(userId, message);
    }

    private Task<string> ProcessBasicCommandAsync(string transcription, ulong userId)
    {
        if (string.IsNullOrWhiteSpace(transcription))
        {
            _logger.LogWarning("Received empty transcription from user {UserId}", userId);
            return Task.FromResult("I didn't hear anything clearly.");
        }

        var normalizedCommand = transcription.ToLowerInvariant().Trim();
        _logger.LogInformation("Processing basic voice command: '{Command}' from user {UserId}", normalizedCommand, userId);

        // Handle "say" commands
        if (IsSayCommand(normalizedCommand))
        {
            var contentToSay = ExtractSayCommandContent(normalizedCommand);
            _logger.LogInformation("Recognized say command from user {UserId}: '{Content}'", userId, contentToSay);
            return Task.FromResult(CreateUserMentionResponse(userId, contentToSay));
        }
        
        // Handle other basic commands
        if (normalizedCommand.Contains("hello") || normalizedCommand.Contains("hi"))
        {
            _logger.LogInformation("Recognized greeting from user {UserId}", userId);
            return Task.FromResult(CreateUserMentionResponse(userId, "Hello there!"));
        }
        
        if (normalizedCommand.Contains("ping"))
        {
            _logger.LogInformation("Recognized ping command from user {UserId}", userId);
            return Task.FromResult(CreateUserMentionResponse(userId, "Pong!"));
        }

        _logger.LogInformation("Unrecognized command: '{Command}' from user {UserId}", normalizedCommand, userId);
        return Task.FromResult(CreateUserMentionResponse(userId, "I don't understand that command. Try saying 'leave', 'playtest', 'play [song]', 'say hello' or 'ping'."));
    }



    private static bool IsSayCommand(string normalizedCommand)
    {
        return normalizedCommand.StartsWith("say ") && normalizedCommand.Length > 4;
    }

    private static string ExtractSayCommandContent(string normalizedCommand)
    {
        return normalizedCommand.Substring(4).Trim();
    }

    private static string CreateUserMentionResponse(ulong userId, string message)
    {
        return $"<@{userId}> {message}";
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