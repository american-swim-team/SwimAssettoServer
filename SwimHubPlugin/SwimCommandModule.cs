using AssettoServer.Commands;
using AssettoServer.Commands.Attributes;
using AssettoServer.Commands.Contexts;
using Qmmands;

namespace SwimHubPlugin;

public class SwimCommandModule : ACModuleBase
{
    [Command("discord", "d", "server", "discord_server")]
    public async Task SwimAsync()
    {
        Reply("https://discord.gg/swimserver");
    }

    [Command("user", "discord_user")]
    public async Task DiscordUserAsync()
    {
        // TODO: Implement
        throw new NotImplementedException();
    }
}