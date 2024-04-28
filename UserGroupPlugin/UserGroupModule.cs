using AssettoServer.Server.Plugin;
using Autofac;
using UserGroupPlugin.Services;


namespace UserGroupPlugin;

/// <summary>
///     This is where the plugin is loaded.
/// </summary>

public class UserGroupModule : AssettoServerModule<UserGroupConfiguration>
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<UserGroupPlugin>().AsSelf().As<IAssettoServerAutostart>().SingleInstance();
        builder.RegisterType<TestUserGroup>().AsSelf();
    }
}
