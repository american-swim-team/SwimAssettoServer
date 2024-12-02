using AssettoServer.Server.Configuration;
using JetBrains.Annotations;
using Serilog;

namespace SwimCrashPlugin;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class SwimCrashConfiguration : IValidateConfiguration<SwimCrashConfigurationValidator>
{
    public float? SpinThreshold { get; init; }
}