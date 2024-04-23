using AssettoServer.Server.Plugin;
using Autofac;
using AssettoServer.Server.UserGroup;

namespace SwimHubPlugin;

public class SwimHubModule : AssettoServerModule<SwimHubConfiguration>
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<SwimUserGroup>().AsSelf();
        builder.RegisterType<SwimUserGroupProvider>().As<IUserGroupProvider>().SingleInstance();
        builder.RegisterType<SwimCommandModule>().AsSelf();
    }
}
