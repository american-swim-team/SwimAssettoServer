using FluentValidation;
using JetBrains.Annotations;
using Serilog;
using System.Text;

namespace SwimCrashPlugin;

[UsedImplicitly]
public class SwimCrashConfigurationValidator : AbstractValidator<SwimCrashConfiguration>
{
    public SwimCrashConfigurationValidator()
    {
        RuleFor(x => x.SpinThreshold).NotEmpty().WithMessage("Server must be set");
    }
}
