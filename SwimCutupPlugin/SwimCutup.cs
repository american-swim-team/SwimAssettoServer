using AssettoServer.Server;
using AssettoServer.Network.Tcp;
using Serilog;
using AssettoServer.Shared.Network.Packets;
using AssettoServer.Server.Configuration;
using System.Numerics;
using System.Text;
using AssettoServer.Shared.Network.Packets.Shared;
using SwimCutupPlugin.Packets;
using System.Text.Json;


namespace SwimCutupPlugin;

public class SwimCutupPlugin
{
    public readonly SwimCutupConfiguration config;
    private readonly CSPClientMessageTypeManager _cspClientMessageTypeManager;
    private readonly EntryCarManager _entryCarManager;
    private readonly ACServerConfiguration _acServerConfiguration;
    public readonly HttpClient http = new HttpClient();
    public readonly string _trackName;
    private Dictionary<ulong, SwimMetricsTracker> _sessions = new();

    public SwimCutupPlugin(SwimCutupConfiguration configuration, CSPClientMessageTypeManager cspClientMessageTypeManager, EntryCarManager entryCarManager, ACServerConfiguration acServerConfiguration)
    {
        Log.Information("------------------------------------");
        Log.Information("SwimCutupPlugin");
        Log.Information("By Romedius");
        Log.Information("------------------------------------");

        config = configuration;
        _cspClientMessageTypeManager = cspClientMessageTypeManager;
        _acServerConfiguration = acServerConfiguration;

        _entryCarManager = entryCarManager;
        _entryCarManager.ClientConnected += OnClientConnected;
        _entryCarManager.ClientDisconnected += OnClientDisconnected;

        _cspClientMessageTypeManager.RegisterClientMessageType(0xBDEC992D, new Action<ACTcpClient, PacketReader>(IncomingCSPEvent));

        _trackName = _acServerConfiguration.Server.Track.Substring(_acServerConfiguration.Server.Track.LastIndexOf('/') + 1);

        System.Timers.Timer timer = new System.Timers.Timer(1000);
        timer.Elapsed += new System.Timers.ElapsedEventHandler(UpdateClientSessions);
        timer.Start();
    }

    private void UpdateClientSessions(object? sender, EventArgs e)
    {
        Dictionary<ACTcpClient, int> nearbyCarsCount = new Dictionary<ACTcpClient, int>();

        foreach (var sessionOuter in _sessions)
        {
            ACTcpClient clientOuter = sessionOuter.Value._client;
            if (!clientOuter.IsConnected || !clientOuter.HasSentFirstUpdate || clientOuter.EntryCar.AiControlled) continue;

            int count = 0;

            foreach (var sessionInner in _sessions)
            {
                ACTcpClient clientInner = sessionInner.Value._client;
                if (!clientInner.IsConnected || !clientInner.HasSentFirstUpdate || clientOuter == clientInner || clientInner.EntryCar.AiControlled) continue;

                if (Vector3.Distance(clientOuter.EntryCar.Status.Position, clientInner.EntryCar.Status.Position) < 200)
                {
                    count++;
                }
            }

            nearbyCarsCount[clientOuter] = count;
        }

        foreach (var session in _sessions)
        {
            ACTcpClient client = session.Value._client;
            if (!client.IsConnected || !client.HasSentFirstUpdate) continue;

            if (nearbyCarsCount.TryGetValue(client, out int count))
            {
                session.Value.UpdateDriverStats(client.EntryCar.Status.Velocity, count);
            }
        }
    }


    private void OnClientConnected(ACTcpClient client, EventArgs args)
    {
        _sessions[client.Guid] = new SwimMetricsTracker(this, client, _trackName);
    }

    private void OnClientDisconnected(ACTcpClient client, EventArgs args)
    {
        _sessions[client.Guid].Dispose();
        _sessions.Remove(client.Guid);
    }

    private void IncomingCSPEvent(ACTcpClient client, PacketReader reader)
    {
        long msgType = reader.Read<long>();
        long payload = reader.Read<long>();
        switch (msgType)
        {
            case 1:
                // get highscore from API
                _sessions[client.Guid].OnHighscoreRequest();
                break;
            case 2:
                // update highscore
                _sessions[client.Guid].OnCollision(payload);
                break;
            default:
                Log.Information("SwimCutupPlugin: Unknown");
                break;
        }
    }
}