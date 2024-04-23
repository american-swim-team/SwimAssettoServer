using AssettoServer.Server.Plugin;
using AssettoServer.Server.OpenSlotFilters;
using Autofac;

namespace SwimWhitelistPlugin;

public class SwimWhitelistModule : AssettoServerModule<SwimWhitelistConfiguration>
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<SwimWhitelistFilter>().As<IOpenSlotFilter>().SingleInstance();
    }
}
