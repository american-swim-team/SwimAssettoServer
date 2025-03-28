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
    }

    public override void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.UseMiddleware<ReverseProxyMiddleware>();
    }
}
