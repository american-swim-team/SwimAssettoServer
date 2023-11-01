using AssettoServer.Server.OpenSlotFilters;
using AssettoServer.Server.Plugin;
using Autofac;

namespace SwimWhitelistPlugin;

public class SwimWhitelistModule : AssettoServerModule<SwimWhitelistConfiguration>
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<SwimWhitelistFilter>().As<IOpenSlotFilter>().SingleInstance();
    }
}
