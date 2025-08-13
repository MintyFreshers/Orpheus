using NetCord.Gateway;

namespace Orpheus.Services.Transcription;

public interface IVoiceCommandProcessor
{
    Task<string> ProcessCommandAsync(string transcription, ulong userId);
    Task<string> ProcessCommandAsync(string transcription, ulong userId, GatewayClient client);
}