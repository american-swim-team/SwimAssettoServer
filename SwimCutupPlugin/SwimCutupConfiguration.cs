using AssettoServer.Server.Configuration;
using JetBrains.Annotations;
using Serilog;

namespace SwimCutupPlugin;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class SwimCutupConfiguration : IValidateConfiguration<SwimCutupConfigurationValidator>
{
    public string? Server { get; init; }
}