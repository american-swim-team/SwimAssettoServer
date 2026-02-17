using FluentValidation;
using JetBrains.Annotations;

namespace SwimGatePlugin;

[UsedImplicitly]
public class SwimGateConfigurationValidator : AbstractValidator<SwimGateConfiguration>
{
    public SwimGateConfigurationValidator()
    {
        RuleFor(x => x.ApiBaseUrl).NotEmpty().WithMessage("ApiBaseUrl must be specified.");
        RuleFor(x => x.ApiKey).NotEmpty().WithMessage("ApiKey must be specified.");
        RuleFor(x => x.AllowedRoles).NotEmpty().WithMessage("At least one allowed role must be specified.");
        RuleFor(x => x.ReservedSlots).GreaterThanOrEqualTo(0).WithMessage("ReservedSlots cannot be negative.");
        RuleFor(x => x.CacheSeconds).GreaterThan(0).WithMessage("CacheSeconds must be greater than 0.");
    }
}
