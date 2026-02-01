using System.Net.Http;
using AssettoServer.Server.GeoParams;
using AssettoServer.Server.Plugin;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Autofac;
using Serilog;

namespace ReverseProxyPlugin;

public class ReverseProxyModule : AssettoServerModule<ReverseProxyConfiguration>
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<ReverseProxyGeoParamsProvider>()
            .As<IGeoParamsProvider>()
            .SingleInstance();

        builder.RegisterType<ReverseProxyTcpHandler>()
           .AsSelf()
           .As<IAssettoServerAutostart>()
           .SingleInstance();

        // Override HttpClient to use custom handler for lobby requests
        // This intercepts lobby registration requests and routes them through the relay
        builder.Register(c =>
        {
            var config = c.Resolve<ReverseProxyConfiguration>();
            if (!string.IsNullOrEmpty(config.LobbyRelayUrl))
            {
                Log.Information("Lobby relay enabled, routing lobby requests through {RelayUrl}", config.LobbyRelayUrl);
                return new HttpClient(new ReverseProxyLobbyHttpHandler(config));
            }
            return new HttpClient();
        }).AsSelf().SingleInstance();
    }

    public override void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.UseMiddleware<ReverseProxyMiddleware>();
    }
}
