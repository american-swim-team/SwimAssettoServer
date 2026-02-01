using System.Text.RegularExpressions;
using AssettoServer.Network.Tcp;
using AssettoServer.Server;
using AssettoServer.Server.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace ReverseProxyPlugin
{
    public class ReverseProxyTcpHandler : BackgroundService
    {
        private readonly ReverseProxyConfiguration _config;
        private readonly EntryCarManager _entryCarManager;
        private readonly CSPServerExtraOptions _extraOptions;

        public ReverseProxyTcpHandler(
            ReverseProxyConfiguration config,
            EntryCarManager entryCarManager,
            CSPServerExtraOptions extraOptions)
        {
            _config = config;
            _entryCarManager = entryCarManager;
            _extraOptions = extraOptions;

            _entryCarManager.ClientConnected += OnClientConnected;
            _extraOptions.CSPServerExtraOptionsSending += OnExtraOptionsSending;

            Log.Information("ReverseProxy TCP handler initialized!");
        }

        private void OnClientConnected(ACTcpClient client, EventArgs _)
        {
            client.HandshakeAccepted += (sender, args) => 
            {
                if (args.HandshakeResponse != null)
                {
                    Log.Verbose("Modifying UDP port in HandshakeAccepted packet.");
                    args.HandshakeResponse.UdpPort = _config.ReverseUdpPort;
                }
            };
        }

        private void OnExtraOptionsSending(ACTcpClient client, CSPServerExtraOptionsSendingEventArgs args)
        {
            try
            {
                var builder = args.Builder;
                var content = builder.ToString();

                if (content.Contains("{ServerIP}") || content.Contains("{ServerHTTPPort}"))
                {
                    var newContent = content
                        .Replace("{ServerIP}", _config.ReverseProxyIp)
                        .Replace("{ServerHTTPPort}", _config.ReverseHttpPort.ToString());

                    builder.Clear();
                    builder.Append(newContent);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "URL rewrite failed for {Client}", client.Name);
            }
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
            _entryCarManager.ClientConnected -= OnClientConnected;
            _extraOptions.CSPServerExtraOptionsSending -= OnExtraOptionsSending;
            base.Dispose();
        }
    }
}