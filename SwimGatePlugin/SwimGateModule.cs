using AssettoServer.Server.OpenSlotFilters;
using AssettoServer.Server.Plugin;
using AssettoServer.Server.UserGroup;
using Autofac;
using Microsoft.Extensions.Hosting;

namespace SwimGatePlugin;

public class SwimGateModule : AssettoServerModule<SwimGateConfiguration>
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<SwimApiClient>().AsSelf().SingleInstance();
        builder.RegisterType<SwimGateFilter>().As<IOpenSlotFilter>().SingleInstance();
        builder.RegisterType<SwimApiUserGroupProvider>().As<IUserGroupProvider>().SingleInstance();
        builder.RegisterType<SwimChatService>().As<IHostedService>().SingleInstance();
    }
}
