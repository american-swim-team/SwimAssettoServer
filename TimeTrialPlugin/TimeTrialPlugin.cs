using System.Reflection;
using AssettoServer.Network.Tcp;
using AssettoServer.Server;
using AssettoServer.Server.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using TimeTrialPlugin.Configuration;
using TimeTrialPlugin.Packets;

namespace TimeTrialPlugin;

public class TimeTrialPlugin : BackgroundService
{
    private readonly TimeTrialConfiguration _configuration;
    private readonly EntryCarManager _entryCarManager;
    private readonly Func<EntryCar, EntryCarTimeTrial> _entryCarTimeTrialFactory;
    private readonly Dictionary<int, EntryCarTimeTrial> _instances = new();

    public TimeTrialPlugin(
        TimeTrialConfiguration configuration,
        ACServerConfiguration serverConfiguration,
        EntryCarManager entryCarManager,
        CSPServerScriptProvider scriptProvider,
        Func<EntryCar, EntryCarTimeTrial> entryCarTimeTrialFactory)
    {
        _configuration = configuration;
        _entryCarManager = entryCarManager;
        _entryCarTimeTrialFactory = entryCarTimeTrialFactory;

        Log.Information("------------------------------------");
        Log.Information("TimeTrialPlugin");
        Log.Information("Loaded {Count} track(s)", configuration.Tracks.Count);
        Log.Information("------------------------------------");

        if (!serverConfiguration.Extra.EnableClientMessages)
        {
            throw new ConfigurationException("TimeTrialPlugin requires ClientMessages to be enabled");
        }

        // Load and inject Lua script from embedded resource
        using var streamReader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("TimeTrialPlugin.lua.timetrial.lua")!);
        scriptProvider.AddScript(streamReader.ReadToEnd(), "timetrial.lua");

        _entryCarManager.ClientConnected += OnClientConnected;
        _entryCarManager.ClientDisconnected += OnClientDisconnected;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var instance in _instances.Values)
        {
            instance.LapCompleted -= OnLapCompleted;
            instance.Dispose();
        }
        _instances.Clear();

        return base.StopAsync(cancellationToken);
    }

    private void OnClientConnected(ACTcpClient client, EventArgs e)
    {
        Log.Debug("TimeTrialPlugin: Client {Name} connected", client.Name);

        // Create instance on-demand if not exists
        if (!_instances.TryGetValue(client.SessionId, out var instance))
        {
            instance = _entryCarTimeTrialFactory(client.EntryCar);
            instance.LapCompleted += OnLapCompleted;
            _instances[client.SessionId] = instance;
        }

        instance.OnClientConnected();

        // Register collision handler using lambda to ensure proper binding
        client.Collision += (sender, args) =>
        {
            if (_instances.TryGetValue(sender.SessionId, out var inst))
            {
                inst.OnCollision();
            }
        };

        // Wait for Lua to be ready before sending packets
        client.LuaReady += OnLuaReady;
    }

    private void OnLapCompleted(object? sender, LapCompletedEventArgs e)
    {
        var message = $"{e.PlayerName} completed {e.TrackName} in {e.FormattedTime}";
        if (e.IsPersonalBest)
        {
            message += " (New PB!)";
        }
        _entryCarManager.BroadcastChat(message);
    }

    private void OnLuaReady(ACTcpClient client, EventArgs e)
    {
        client.LuaReady -= OnLuaReady;

        if (_instances.TryGetValue(client.SessionId, out var instance))
        {
            instance.SendTrackInfo();
        }
    }

    private void OnClientDisconnected(ACTcpClient client, EventArgs e)
    {
        // Note: Can't easily unsubscribe lambda, but client is disconnecting anyway
    }

    internal EntryCarTimeTrial? GetTimeTrial(EntryCar entryCar)
    {
        return _instances.TryGetValue(entryCar.SessionId, out var instance) ? instance : null;
    }
}
