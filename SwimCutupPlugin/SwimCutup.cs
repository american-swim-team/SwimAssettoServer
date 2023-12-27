using AssettoServer.Server;
using AssettoServer.Network.Tcp;
using Serilog;
using AssettoServer.Shared.Network.Packets;
using AssettoServer.Server.Configuration;
using System.Text;
using AssettoServer.Shared.Network.Packets.Shared;
using SwimCutupPlugin.Packets;

namespace SwimCutupPlugin;

public class SwimCutupPlugin
{
    private readonly SwimCutupConfiguration _config;
    private readonly CSPClientMessageTypeManager _cspClientMessageTypeManager;


    public SwimCutupPlugin(SwimCutupConfiguration SwimCutupConfiguration, CSPClientMessageTypeManager cspClientMessageTypeManager)
    {
        Log.Information("------------------------------------");
        Log.Information("SwimCutupPlugin");
        Log.Information("By Romedius");
        Log.Information("------------------------------------");

        _config = SwimCutupConfiguration;
        _cspClientMessageTypeManager = cspClientMessageTypeManager;

        _cspClientMessageTypeManager.RegisterClientMessageType(0x953A6FB5, new Action<ACTcpClient, PacketReader>(IncomingCSPEvent));
    }

    private void IncomingCSPEvent(ACTcpClient client, PacketReader reader)
    {
        MsgType msgType = reader.Read<MsgType>();
        long payload = reader.Read<long>();

        switch (msgType)
        {
            case MsgType.Initialize:
                Log.Information("SwimCutupPlugin: Initialize");
                break;
            case MsgType.NewHighscore:
                Log.Information("SwimCutupPlugin: NewHighscore");
                break;
            default:
                Log.Information("SwimCutupPlugin: Unknown");
                break;
        }
    }
}