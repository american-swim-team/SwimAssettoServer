using System.Reflection;
using System.Text.Json;
using AssettoServer.Network.Tcp;
using AssettoServer.Server;
using AssettoServer.Server.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using TimeTrialPlugin.Configuration;
using TimeTrialPlugin.Packets;
using TimeTrialPlugin.Timing;

namespace TimeTrialPlugin;

public class TimeTrialPlugin : BackgroundService
{
    private readonly TimeTrialConfiguration _configuration;
    private readonly EntryCarManager _entryCarManager;
    private readonly LeaderboardManager _leaderboardManager;
    private readonly Func<EntryCar, EntryCarTimeTrial> _entryCarTimeTrialFactory;
    private readonly Dictionary<int, EntryCarTimeTrial> _instances = new();

    public TimeTrialPlugin(
        TimeTrialConfiguration configuration,
        ACServerConfiguration serverConfiguration,
        EntryCarManager entryCarManager,
        CSPServerScriptProvider scriptProvider,
        LeaderboardManager leaderboardManager,
        Func<EntryCar, EntryCarTimeTrial> entryCarTimeTrialFactory)
    {
        _configuration = configuration;
        _entryCarManager = entryCarManager;
        _leaderboardManager = leaderboardManager;
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
            _instances[client.SessionId] = instance;
        }

        instance.OnClientConnected();

        // Register collision handler
        client.Collision += OnCollision;

        // Wait for Lua to be ready before sending packets
        client.LuaReady += OnLuaReady;
    }

    private void OnLuaReady(ACTcpClient client, EventArgs e)
    {
        client.LuaReady -= OnLuaReady;

        if (_instances.TryGetValue(client.SessionId, out var instance))
        {
            instance.SendTrackInfo();
        }

        // Send leaderboards for all tracks
        foreach (var track in _configuration.Tracks)
        {
            SendLeaderboardToClient(client, track.Id);
        }
    }

    private void OnClientDisconnected(ACTcpClient client, EventArgs e)
    {
        client.Collision -= OnCollision;
    }

    private void OnCollision(ACTcpClient sender, CollisionEventArgs e)
    {
        if (_instances.TryGetValue(sender.SessionId, out var instance))
        {
            instance.OnCollision();
        }
    }

    public void BroadcastLeaderboard(string trackId)
    {
        var leaderboard = _leaderboardManager.GetLeaderboard(trackId);
        var entriesJson = JsonSerializer.Serialize(leaderboard.Select(lt => new
        {
            lt.PlayerName,
            lt.CarModel,
            lt.TotalTimeMs,
            FormattedTime = lt.FormattedTotalTime
        }));

        var packet = new LeaderboardUpdatePacket
        {
            TrackId = trackId,
            EntriesJson = entriesJson
        };

        foreach (var entryCar in _entryCarManager.EntryCars)
        {
            entryCar.Client?.SendPacket(packet);
        }
    }

    private void SendLeaderboardToClient(ACTcpClient client, string trackId)
    {
        var leaderboard = _leaderboardManager.GetLeaderboard(trackId);
        var entriesJson = JsonSerializer.Serialize(leaderboard.Select(lt => new
        {
            lt.PlayerName,
            lt.CarModel,
            lt.TotalTimeMs,
            FormattedTime = lt.FormattedTotalTime
        }));

        client.SendPacket(new LeaderboardUpdatePacket
        {
            TrackId = trackId,
            EntriesJson = entriesJson
        });
    }

    internal EntryCarTimeTrial? GetTimeTrial(EntryCar entryCar)
    {
        return _instances.TryGetValue(entryCar.SessionId, out var instance) ? instance : null;
    }
}
