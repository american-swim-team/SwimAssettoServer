using AssettoServer.Server.Configuration;
using JetBrains.Annotations;

namespace SwimGatePlugin;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class SwimGateConfiguration : IValidateConfiguration<SwimGateConfigurationValidator>
{
    public string ApiBaseUrl { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public List<string> AllowedRoles { get; init; } = [];
    public int ReservedSlots { get; init; } = 0;
    public string RejectMessage { get; init; } = "You do not have permission to join this server.";
    public int CacheSeconds { get; init; } = 60;
    public bool FailClosed { get; init; } = false;
}
