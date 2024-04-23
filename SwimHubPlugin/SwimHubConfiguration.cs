using AssettoServer.Server.Configuration;
using JetBrains.Annotations;

namespace SwimHubPlugin;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class SwimHubConfiguration : IValidateConfiguration<SwimHubConfigurationValidator>
{
    public Uri? Api { get; init; }
}