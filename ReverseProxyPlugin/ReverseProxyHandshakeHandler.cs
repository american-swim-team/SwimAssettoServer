using AssettoServer.Network.Tcp;
using AssettoServer.Server.Plugin;
using AssettoServer.Server;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace ReverseProxyPlugin
{
    public class ReverseProxyHandshakeHandler : BackgroundService, IAssettoServerAutostart
    {
        private readonly ReverseProxyConfiguration _config;
        private readonly EntryCarManager _entryCarManager;

        public ReverseProxyHandshakeHandler(ReverseProxyConfiguration config, EntryCarManager entryCarManager)
        {
            _config = config;
            _entryCarManager = entryCarManager;

            _entryCarManager.ClientConnected += OnClientConnected;
        }

        private void OnClientConnected(ACTcpClient client, EventArgs _)
        {
            client.HandshakeAccepted += (sender, args) => 
            {
                if (args.HandshakeResponse != null)
                {
                    args.HandshakeResponse.UdpPort = _config.ReverseUdpPort;
                }
            };
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // No background work needed, just return completed task
            return Task.CompletedTask; 
        }
    }
}