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

public class TimeTrialPlugin : IHostedService
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

        // Load and inject Lua script
        var luaPath = Path.Join(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "lua", "timetrial.lua");

        using var streamReader = new StreamReader(luaPath);
        var script = streamReader.ReadToEnd();
        scriptProvider.AddScript(script, "timetrial.lua", new Dictionary<string, object>
        {
            ["showLeaderboard"] = _configuration.ShowLeaderboard ? "true" : "false",
            ["leaderboardSize"] = _configuration.LeaderboardSize.ToString()
        });

        _entryCarManager.ClientConnected += OnClientConnected;
        _entryCarManager.ClientDisconnected += OnClientDisconnected;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Create per-car instances
        foreach (var entryCar in _entryCarManager.EntryCars)
        {
            _instances[entryCar.SessionId] = _entryCarTimeTrialFactory(entryCar);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var instance in _instances.Values)
        {
            instance.Dispose();
        }
        _instances.Clear();

        return Task.CompletedTask;
    }

    private void OnClientConnected(ACTcpClient client, EventArgs e)
    {
        Log.Debug("TimeTrialPlugin: Client {Name} connected", client.Name);

        if (_instances.TryGetValue(client.SessionId, out var instance))
        {
            instance.OnClientConnected();
        }

        // Register collision handler
        client.Collision += OnCollision;

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

    private void OnCollision(ACTcpClient? sender, CollisionEventArgs e)
    {
        if (sender == null) return;

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
