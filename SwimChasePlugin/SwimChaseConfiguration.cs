using AssettoServer.Server.Configuration;
using JetBrains.Annotations;
using Serilog;

namespace SwimChasePlugin;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class SwimChaseConfiguration : IValidateConfiguration<SwimWhitelistConfigurationValidator>
{
    public Uri? EndpointUrl { get; init; }
    public int CollisionsPer25 { get; init; }
    public int CaptureTime { get; init; }
    public int CaptureRadius { get; init; }
    public int DetectionRadius { get; init; }
    public bool HideCops { get; init; }
}