using AssettoServer.Server.Configuration;
using JetBrains.Annotations;
using Serilog;

namespace SwimWhitelistPlugin;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class SwimWhitelistConfiguration : IValidateConfiguration<SwimWhitelistConfigurationValidator>
{
    public Uri? EndpointUrl { get; init; }
    public int ReservedSlots { get; init; }
    public List<long> ReservedSlotsRoles { get; init; } = new();
    public List<ReservedCar> ReservedCars { get; init; } = new();
}

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public struct ReservedCar
{
    public string Model { get; set; }
    public int Amount { get; set; }
    public List<long> Roles { get; set; }
}
