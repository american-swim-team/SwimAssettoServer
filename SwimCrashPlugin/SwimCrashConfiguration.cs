using AssettoServer.Server.Configuration;
using JetBrains.Annotations;
using Serilog;

namespace SwimCrashPlugin;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class SwimCrashConfiguration : IValidateConfiguration<SwimCrashConfigurationValidator>
{
    public float? SpinThreshold { get; init; }
    public float? FlipThreshold { get; init; }
    public float? SpeedThreshold { get; init; }
    public int? MonitorTime { get; init; }
    public float? CruisingSpeedThreshold { get; init; }
    public int? ReEnableCooldownMs { get; init; }
    public int? MaxNoCollisionTimeMs { get; init; }
}
