using NetCord.Gateway;

namespace Orpheus.Services.VoiceClientController;

public interface IVoiceClientController
{
    Task<string> JoinVoiceChannelOfUserAsync(Guild guild, GatewayClient client, ulong userId);
    Task<string> LeaveVoiceChannelAsync(Guild guild, GatewayClient client);
    Task<string> StartEchoingAsync(Guild guild, GatewayClient client, ulong userId);
    Task<string> PlayMp3Async(Guild guild, GatewayClient client, ulong userId, string filePath);
    Task<string> PlayOverlayMp3Async(Guild guild, GatewayClient client, ulong userId, string filePath);
    Task<string> StopPlaybackAsync();
}