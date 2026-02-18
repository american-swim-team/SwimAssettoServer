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
    public List<ChatRoleConfig> ChatRoles { get; init; } = [];
    public ProfanityFilterConfig ProfanityFilter { get; init; } = new();
}

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class ChatRoleConfig
{
    public string Role { get; init; } = "";
    public string Color { get; init; } = "#FFFFFF";
    public int Priority { get; init; } = 100;
}

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class ProfanityFilterConfig
{
    public bool Enabled { get; init; } = false;
    public List<string> CustomWords { get; init; } = [];
    public string Replacement { get; init; } = "***";
}
