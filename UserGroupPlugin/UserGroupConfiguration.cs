using AssettoServer.Server.Configuration;
using JetBrains.Annotations;

namespace UserGroupPlugin;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
public class UserGroupConfiguration : IValidateConfiguration<UserGroupConfigurationValidator>
{
    public string TestData { get; set; } = null!;
}
