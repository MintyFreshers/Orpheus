using Microsoft.Extensions.Logging;
using NetCord.Gateway;
using Orpheus.Services.Queue;
using Orpheus.Services.Downloader.Youtube;
using Orpheus.Services;
using System.Linq;

namespace Orpheus.Services.Transcription;

public class VoiceCommandProcessor : IVoiceCommandProcessor
{
    private readonly ILogger<VoiceCommandProcessor> _logger;
    private readonly ISongQueueService _queueService;
    private readonly IQueuePlaybackService _queuePlaybackService;
    private readonly IYouTubeDownloader _downloader;
    private readonly IMessageUpdateService _messageUpdateService;

    public VoiceCommandProcessor(
        ILogger<VoiceCommandProcessor> logger,
        ISongQueueService queueService,
        IQueuePlaybackService queuePlaybackService,
        IYouTubeDownloader downloader,
        IMessageUpdateService messageUpdateService)
    {
        _logger = logger;
        _queueService = queueService;
        _queuePlaybackService = queuePlaybackService;
        _downloader = downloader;
        _messageUpdateService = messageUpdateService;
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

        // Get guild context from the client cache
        // For voice commands, we should be in a guild context since the user is in a voice channel
        Guild? guild = null;
        try
        {
            // Find the guild where the user is currently in a voice channel
            guild = client.Cache.Guilds.Values.FirstOrDefault(g => 
                g.VoiceStates.ContainsKey(userId) && g.VoiceStates[userId].ChannelId != null);
            
            if (guild == null)
            {
                _logger.LogWarning("Could not find guild with user {UserId} in voice channel", userId);
                return CreateUserMentionResponse(userId, "I couldn't determine which server you're in. Make sure you're in a voice channel.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get guild context for voice command");
            return CreateUserMentionResponse(userId, "I couldn't determine the server context for this command.");
        }

        // Parse and execute basic commands only
        // Advanced voice control commands (play, leave, playtest) will be handled at a higher level
        // to avoid circular dependency issues
        return await ProcessBasicCommandAsync(normalizedCommand, userId);
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

        _logger.LogInformation("Unrecognized basic command: '{Command}' from user {UserId}", normalizedCommand, userId);
        return Task.FromResult(CreateUserMentionResponse(userId, "I don't understand that basic command. Try saying 'say hello', 'hello', or 'ping'."));
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
}