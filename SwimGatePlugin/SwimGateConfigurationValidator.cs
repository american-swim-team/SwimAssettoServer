using System.Drawing;
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

        RuleForEach(x => x.ChatRoles).ChildRules(role =>
        {
            role.RuleFor(r => r.Role).NotEmpty().WithMessage("ChatRole Role must not be empty.");
            role.RuleFor(r => r.Color).Must(BeValidHexColor).WithMessage("ChatRole Color must be a valid hex color (e.g. #FF4444).");
            role.RuleFor(r => r.Priority).GreaterThanOrEqualTo(0).WithMessage("ChatRole Priority must be >= 0.");
        });

        RuleFor(x => x.ProfanityFilter).ChildRules(pf =>
        {
            pf.RuleFor(p => p.Replacement).NotEmpty()
                .When(p => p.Enabled)
                .WithMessage("ProfanityFilter Replacement must not be empty when filter is enabled.");
        });
    }

    private static bool BeValidHexColor(string color)
    {
        try
        {
            ColorTranslator.FromHtml(color);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
