using FluentValidation;
using JetBrains.Annotations;
using Serilog;
using System.Text;

namespace SwimCutupPlugin;

[UsedImplicitly]
public class SwimCutupConfigurationValidator : AbstractValidator<SwimCutupConfiguration>
{
    public SwimCutupConfigurationValidator()
    {
        RuleFor(x => x.Server).NotEmpty().WithMessage("Server must be set");
    }
}
