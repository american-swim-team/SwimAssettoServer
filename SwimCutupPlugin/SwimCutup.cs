using AssettoServer.Server;
using AssettoServer.Network.Tcp;
using Serilog;
using AssettoServer.Shared.Network.Packets;
using AssettoServer.Server.Configuration;
using System.Text;
using AssettoServer.Shared.Network.Packets.Shared;
using SwimCutupPlugin.Packets;
using System.Text.Json;


namespace SwimCutupPlugin;

public class SwimCutupPlugin
{
    private readonly SwimCutupConfiguration _config;
    private readonly CSPClientMessageTypeManager _cspClientMessageTypeManager;
    private readonly ACServerConfiguration _acServerConfiguration;
    private readonly HttpClient _http = new HttpClient();
    private readonly string _trackName;


    public SwimCutupPlugin(SwimCutupConfiguration configuration, CSPClientMessageTypeManager cspClientMessageTypeManager, ACServerConfiguration acServerConfiguration)
    {
        Log.Information("------------------------------------");
        Log.Information("SwimCutupPlugin");
        Log.Information("By Romedius");
        Log.Information("------------------------------------");

        _config = configuration;
        _cspClientMessageTypeManager = cspClientMessageTypeManager;
        _acServerConfiguration = acServerConfiguration;

        _cspClientMessageTypeManager.RegisterClientMessageType(0xBDEC992D, new Action<ACTcpClient, PacketReader>(IncomingCSPEvent));

        _trackName = _acServerConfiguration.Server.Track.Substring(_acServerConfiguration.Server.Track.LastIndexOf('/') + 1);
        Log.Information("SwimCutupPlugin: Track: {trackName}", _trackName);
    }

    private void IncomingCSPEvent(ACTcpClient client, PacketReader reader)
    {
        long msgType = reader.Read<long>();
        long payload = reader.Read<long>();
        Log.Information("SwimCutupPlugin: IncomingCSPEvent: msgType: {msgType}, payload: {payload}", msgType, payload);
        switch (msgType)
        {
            case 1:
                // get highscore from API
                var highscore_request = new StringContent(JsonSerializer.Serialize(new { steamid = client.Guid, track = _trackName, car = client.EntryCar.Model }), Encoding.UTF8, "application/json");
                var highscore_response = _http.PostAsync(_config.Server + "/fetch_cutup_score", highscore_request).Result.Content.ReadAsStringAsync().Result;
                var highscore =  JsonSerializer.Deserialize<Dictionary<string, long>>(highscore_response);
                client.SendPacket(new SwimCutupMsg { MsgType = 1, Payload = highscore["data"] });
                break;
            case 2:
                var new_highscore_request = new StringContent(JsonSerializer.Serialize(new { steamid = client.Guid, track = _trackName, car = client.EntryCar.Model, score = payload }), Encoding.UTF8, "application/json");
                var new_highscore_response = _http.PostAsync(_config.Server + "/insert_cutup_score", new_highscore_request).Result.Content.ReadAsStringAsync().Result;
                var status =  JsonSerializer.Deserialize<Dictionary<string, string>>(new_highscore_response);
                if (status["status"] == "ERROR") {
                    Log.Information("SwimCutupPlugin: Error updating highscore: {status}", status["message"]);
                }
                break;
            default:
                Log.Information("SwimCutupPlugin: Unknown");
                break;
        }
    }
}