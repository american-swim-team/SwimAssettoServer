using System.Threading.Tasks;
using AssettoServer.Server.Plugin;
using AssettoServer.Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace UserGroupPlugin;

/// <summary>
///     This is where the plugin background service is started
/// </summary>

public class UserGroupPlugin : CriticalBackgroundService, IAssettoServerAutostart
{
    public UserGroupPlugin(IHostApplicationLifetime applicationLifetime) : base(applicationLifetime)
    {
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Use the plugin classes from one of the other plugins as an example of what to do here

        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during user group plugin execution.");
        }
        finally
        {
            await Task.Delay(1000, stoppingToken);
        }
    }
}
