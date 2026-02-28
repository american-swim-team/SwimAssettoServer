using FluentValidation;
using JetBrains.Annotations;

#pragma warning disable CS0618 // Obsolete members used for backward compat validation
namespace TrafficAiPlugin.Configuration;

// Use FluentValidation to validate plugin configuration
[UsedImplicitly]
public class TrafficAiConfigurationValidator : AbstractValidator<TrafficAiConfiguration>
{
    public TrafficAiConfigurationValidator()
    {
        // Validate legacy point-based params only if explicitly set
        RuleFor(ai => ai.MinSpawnDistancePoints).LessThanOrEqualTo(ai => ai.MaxSpawnDistancePoints)
            .When(ai => ai.MinSpawnDistancePoints > 0 && ai.MaxSpawnDistancePoints > 0);
        // Validate new meter-based params
        RuleFor(ai => ai.MinSpawnDistanceMeters).LessThanOrEqualTo(ai => ai.MaxSpawnDistanceMeters)
            .When(ai => ai.MinSpawnDistanceMeters > 0 && ai.MaxSpawnDistanceMeters > 0);
        RuleFor(ai => ai.MinTrafficGapMeters).LessThanOrEqualTo(ai => ai.MaxTrafficGapMeters)
            .When(ai => ai.MinTrafficGapMeters > 0 && ai.MaxTrafficGapMeters > 0);
        // Validate legacy safety distance only if explicitly set
        RuleFor(ai => ai.MinAiSafetyDistanceMeters).LessThanOrEqualTo(ai => ai.MaxAiSafetyDistanceMeters)
            .When(ai => ai.MinAiSafetyDistanceMeters > 0 && ai.MaxAiSafetyDistanceMeters > 0);
        RuleFor(ai => ai.MinSpawnProtectionTimeSeconds).LessThanOrEqualTo(ai => ai.MaxSpawnProtectionTimeSeconds);
        RuleFor(ai => ai.MinCollisionStopTimeSeconds).LessThanOrEqualTo(ai => ai.MaxCollisionStopTimeSeconds);
        RuleFor(ai => ai.MaxSpeedVariationPercent).InclusiveBetween(0, 1);
        RuleFor(ai => ai.DefaultAcceleration).GreaterThan(0);
        RuleFor(ai => ai.DefaultDeceleration).GreaterThan(0);
        RuleFor(ai => ai.NamePrefix).NotNull();
        RuleFor(ai => ai.IgnoreObstaclesAfterSeconds).GreaterThanOrEqualTo(0);
        RuleFor(ai => ai.HourlyTrafficDensity)
            .Must(htd => htd?.Count == 24)
            .When(ai => ai.HourlyTrafficDensity != null)
            .WithMessage("HourlyTrafficDensity must have exactly 24 entries");
        RuleFor(ai => ai.MultiAnticipationCount).InclusiveBetween(1, 3);
        RuleFor(ai => ai.MultiAnticipationDecay).InclusiveBetween(0.1f, 0.9f);
        RuleFor(ai => ai.DriveOffDelayMinSeconds).GreaterThanOrEqualTo(0);
        RuleFor(ai => ai.DriveOffDelayMaxSeconds).GreaterThanOrEqualTo(ai => ai.DriveOffDelayMinSeconds);
        RuleFor(ai => ai.DriveOffRampSeconds).GreaterThan(0);
        RuleFor(ai => ai.CarSpecificOverrides).NotNull();
        RuleFor(ai => ai.AiBehaviorUpdateIntervalHz).GreaterThan(0);
        RuleFor(ai => ai.LaneCountSpecificOverrides).NotNull();
        RuleForEach(ai => ai.LaneCountSpecificOverrides).ChildRules(overrides =>
        {
            overrides.RuleFor(o => o.Key).GreaterThan(0);
            overrides.RuleFor(o => o.Value.MinAiSafetyDistanceMeters).LessThanOrEqualTo(o => o.Value.MaxAiSafetyDistanceMeters);
        });
    }
}
