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
        RuleFor(x => x.SpinThreshold).NotEmpty().WithMessage("SpinThreshold must be set");
        RuleFor(x => x.SpeedThreshold).NotEmpty().WithMessage("SpeedThreshold must be set");
        RuleFor(x => x.FlipThreshold).NotEmpty().WithMessage("FlipThreshold must be set");
        RuleFor(x => x.MonitorTime).NotEmpty().WithMessage("MonitorTime must be set");
    }
}
