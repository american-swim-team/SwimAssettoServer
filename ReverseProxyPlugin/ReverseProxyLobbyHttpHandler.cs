using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Serilog;

namespace ReverseProxyPlugin
{
    public class ReverseProxyLobbyHttpHandler : DelegatingHandler
    {
        private readonly ReverseProxyConfiguration _config;
        private static readonly Regex ServerNamePortRegex = new(@"ℹ\d+$", RegexOptions.Compiled);

        public ReverseProxyLobbyHttpHandler(ReverseProxyConfiguration config)
        {
            _config = config;
            InnerHandler = new HttpClientHandler();
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (IsLobbyRequest(request.RequestUri) && !string.IsNullOrEmpty(_config.LobbyRelayUrl))
            {
                var originalUri = request.RequestUri;
                request.RequestUri = RewriteToRelay(originalUri!);
                Log.Debug("Rewrote lobby request from {Original} to {Rewritten}", originalUri, request.RequestUri);
            }

            return await base.SendAsync(request, cancellationToken);
        }

        private bool IsLobbyRequest(Uri? uri)
        {
            if (uri == null) return false;
            return uri.Host == "93.57.10.21" ||
                   uri.Host == "lobby.assettocorsa.net";
        }

        private Uri RewriteToRelay(Uri original)
        {
            // Transform: http://93.57.10.21/lobby.ashx/register?port=9000&tcp_port=9000&name=...
            // To:        http://relay/relay-lobby/register?port=9000&tcp_port=9000&name=...
            // Also rewrite port params to use ReverseUdpPort/ReverseTcpPort

            var relayBase = new Uri(_config.LobbyRelayUrl!);

            // Extract action from path (e.g., "register" or "ping" from "/lobby.ashx/register")
            var pathMatch = Regex.Match(original.AbsolutePath, @"/lobby\.ashx/(\w+)");
            var action = pathMatch.Success ? pathMatch.Groups[1].Value : "register";

            // Parse and rewrite query parameters
            var query = HttpUtility.ParseQueryString(original.Query);

            // Rewrite ports to proxy ports
            if (query["port"] != null)
            {
                query["port"] = _config.ReverseUdpPort.ToString();
            }
            if (query["tcp_port"] != null)
            {
                query["tcp_port"] = _config.ReverseTcpPort.ToString();
            }

            // Rewrite server name port suffix if present (e.g., "Server Name ℹ8081" -> "Server Name ℹ8081")
            if (query["name"] != null)
            {
                var name = query["name"];
                if (ServerNamePortRegex.IsMatch(name))
                {
                    query["name"] = ServerNamePortRegex.Replace(name, $"ℹ{_config.ReverseHttpPort}");
                }
            }

            // Build new URI
            var builder = new UriBuilder(relayBase)
            {
                Path = relayBase.AbsolutePath.TrimEnd('/') + "/" + action,
                Query = query.ToString()
            };

            return builder.Uri;
        }
    }
}
