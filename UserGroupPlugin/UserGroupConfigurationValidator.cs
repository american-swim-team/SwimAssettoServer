using FluentValidation;
using JetBrains.Annotations;

namespace UserGroupPlugin;

[UsedImplicitly]
public class UserGroupConfigurationValidator : AbstractValidator<UserGroupConfiguration>
{
    public UserGroupConfigurationValidator()
    {
        RuleFor(cfg => cfg.TestData).NotEmpty();
    }
}
