using AssettoServer.Server.Plugin;
using Autofac;

namespace SwimCrashPlugin;

public class SwimCrashModule : AssettoServerModule<SwimCrashConfiguration>
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<SwimCrashHandler>().AsSelf().AutoActivate().SingleInstance();
    }
}