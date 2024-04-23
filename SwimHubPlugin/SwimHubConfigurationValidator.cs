using FluentValidation;
using JetBrains.Annotations;

namespace SwimHubPlugin;

[UsedImplicitly]
public class SwimHubConfigurationValidator : AbstractValidator<SwimHubConfiguration>
{
    public SwimHubConfigurationValidator()
    {
        RuleFor(x => x.Api).NotNull();
    }
}
