using FluentValidation;
using JetBrains.Annotations;

namespace SwimWhitelistPlugin;

[UsedImplicitly]
public class SwimWhitelistConfigurationValidator : AbstractValidator<SwimWhitelistConfiguration>
{
    public SwimWhitelistConfigurationValidator()
    {
        RuleFor(x => x.EndpointUrl).NotEmpty().WithMessage("Endpoint URL cannot be empty.");
        RuleFor(x => x.ReservedSlots).GreaterThanOrEqualTo(0).WithMessage("Reserved slots cannot be negative.");
        RuleForEach(x => x.ReservedCars).ChildRules(sr => 
        {
            sr.RuleFor(x => x.Model).NotEmpty().WithMessage("Car name cannot be empty.");
            sr.RuleFor(x => x.Amount).GreaterThanOrEqualTo(0).WithMessage("Reserved slots cannot be negative.");
            sr.RuleFor(x => x.Roles).NotEmpty().WithMessage("At least one role must be specified.");
        });
    }
}
