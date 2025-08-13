using Microsoft.Extensions.Configuration;

namespace Orpheus.Configuration;

public class BotConfiguration
{
    private readonly IConfiguration _configuration;

    public BotConfiguration(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public ulong DefaultChannelId => GetChannelId("Discord:DefaultChannelId", 738893202706268292UL);

    private ulong GetChannelId(string configKey, ulong defaultValue)
    {
        var configValue = _configuration[configKey];
        if (string.IsNullOrEmpty(configValue))
        {
            return defaultValue;
        }

        if (ulong.TryParse(configValue, out var parsedValue))
        {
            return parsedValue;
        }

        return defaultValue;
    }
}
