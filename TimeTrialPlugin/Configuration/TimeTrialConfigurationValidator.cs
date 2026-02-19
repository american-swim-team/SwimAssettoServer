using FluentValidation;
using JetBrains.Annotations;

namespace TimeTrialPlugin.Configuration;

[UsedImplicitly]
public class TimeTrialConfigurationValidator : AbstractValidator<TimeTrialConfiguration>
{
    public TimeTrialConfigurationValidator()
    {
        RuleFor(c => c.MinStartSpeedKph).GreaterThanOrEqualTo(0);
        RuleFor(c => c.MaxSectorTimeSeconds).GreaterThan(0);
        RuleFor(c => c.TeleportThresholdMeters).GreaterThan(0);
        RuleFor(c => c.BestTimesFilePath).NotEmpty();

        RuleForEach(c => c.Tracks).ChildRules(track =>
        {
            track.RuleFor(t => t.Id).NotEmpty();
            track.RuleFor(t => t.Name).NotEmpty();
            track.RuleFor(t => t.Checkpoints).NotEmpty().WithMessage("Each track must have at least one checkpoint");
            track.RuleFor(t => t.Checkpoints)
                .Must(checkpoints =>
                    checkpoints.Any(c => c.Type == CheckpointType.StartFinish) ||
                    (checkpoints.Any(c => c.Type == CheckpointType.Start) && checkpoints.Any(c => c.Type == CheckpointType.Finish)))
                .WithMessage("Each track must have either a StartFinish checkpoint OR both Start and Finish checkpoints");

            track.RuleForEach(t => t.Checkpoints).ChildRules(checkpoint =>
            {
                checkpoint.RuleFor(c => c.Position)
                    .Must(p => p.Count == 3)
                    .WithMessage("Position must have exactly 3 values [X, Y, Z]");
                checkpoint.RuleFor(c => c.Radius).GreaterThan(0);
                checkpoint.RuleFor(c => c.Direction)
                    .Must(d => d!.Count == 3)
                    .When(c => c.Direction != null)
                    .WithMessage("Direction must have exactly 3 values [X, Y, Z]");
                checkpoint.RuleFor(c => c.DirectionToleranceDegrees)
                    .GreaterThan(0).LessThanOrEqualTo(180)
                    .WithMessage("DirectionToleranceDegrees must be between 0 (exclusive) and 180 (inclusive)");
            });
        });
    }
}
