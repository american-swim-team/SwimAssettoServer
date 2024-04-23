using AssettoServer.Server.Plugin;
using Autofac;

namespace SwimCutupPlugin;

public class SwimCutupModule : AssettoServerModule<SwimCutupConfiguration>
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<SwimCutupPlugin>().AsSelf().AutoActivate().SingleInstance();
    }
}
