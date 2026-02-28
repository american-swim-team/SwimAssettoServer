using TrafficAiPlugin.Configuration;

namespace TrafficAiPlugin.Brain;

/// <summary>
/// Factory for generating randomized driver personalities with correlated traits.
/// Uses a simplified two-parameter system: variety (spread) and bias (aggressiveness tendency).
/// </summary>
public class PersonalityFactory
{
    private readonly TrafficAiConfiguration _config;

    // Hardcoded trait ranges (simplified from 14 config options)
    private const float MinAggressiveness = 0.7f;
    private const float MaxAggressiveness = 1.3f;
    private const float MinPatience = 0.6f;
    private const float MaxPatience = 1.8f;
    private const float MinDesiredSpeedFactor = 0.9f;
    private const float MaxDesiredSpeedFactor = 1.1f;
    private const float MinFollowingDistanceFactor = 0.8f;
    private const float MaxFollowingDistanceFactor = 1.4f;
    private const float MinAccelerationFactor = 0.8f;
    private const float MaxAccelerationFactor = 1.2f;
    private const float MinDecelerationFactor = 0.85f;
    private const float MaxDecelerationFactor = 1.15f;
    private const float MinReactionTimeFactor = 0.7f;
    private const float MaxReactionTimeFactor = 1.3f;
    private const float MinDriveOffDelayFactor = 0.6f;
    private const float MaxDriveOffDelayFactor = 1.4f;

    public PersonalityFactory(TrafficAiConfiguration config)
    {
        _config = config;
    }

    /// <summary>
    /// Generate a randomized personality using variety and bias parameters.
    /// </summary>
    /// <returns>A new DriverPersonality with randomized but correlated traits</returns>
    public DriverPersonality Create()
    {
        float variety = Math.Clamp(_config.PersonalityVariety, 0f, 1f);
        float bias = Math.Clamp(_config.PersonalityBias, -1f, 1f);

        // Base temperament: bias shifts center, variety controls spread
        // temperament 0 = calm, 1 = aggressive
        float center = 0.5f + bias * 0.3f;
        float spread = variety * 0.5f;
        float temperament = center + (Random.Shared.NextSingle() - 0.5f) * 2.0f * spread;
        temperament = Math.Clamp(temperament, 0f, 1f);

        // Per-trait variance (how much each trait can deviate from temperament)
        float traitVariance = variety * 0.15f;

        return new DriverPersonality
        {
            // Aggressive drivers have high aggressiveness
            Aggressiveness = GenerateTraitFromTemperament(
                temperament, traitVariance,
                MinAggressiveness, MaxAggressiveness,
                correlationDirection: 1),

            // Aggressive drivers have LOW patience (inverse correlation)
            Patience = GenerateTraitFromTemperament(
                temperament, traitVariance,
                MinPatience, MaxPatience,
                correlationDirection: -1),

            // Aggressive drivers want higher speeds
            DesiredSpeedFactor = GenerateTraitFromTemperament(
                temperament, traitVariance,
                MinDesiredSpeedFactor, MaxDesiredSpeedFactor,
                correlationDirection: 1),

            // Aggressive drivers keep shorter following distances (inverse)
            FollowingDistanceFactor = GenerateTraitFromTemperament(
                temperament, traitVariance,
                MinFollowingDistanceFactor, MaxFollowingDistanceFactor,
                correlationDirection: -1),

            // Aggressive drivers accelerate harder
            AccelerationFactor = GenerateTraitFromTemperament(
                temperament, traitVariance,
                MinAccelerationFactor, MaxAccelerationFactor,
                correlationDirection: 1),

            // Aggressive drivers brake harder
            DecelerationFactor = GenerateTraitFromTemperament(
                temperament, traitVariance,
                MinDecelerationFactor, MaxDecelerationFactor,
                correlationDirection: 1),

            // Aggressive drivers react faster (inverse - lower factor = faster reaction)
            ReactionTimeFactor = GenerateTraitFromTemperament(
                temperament, traitVariance,
                MinReactionTimeFactor, MaxReactionTimeFactor,
                correlationDirection: -1),

            // Aggressive drivers drive off sooner (inverse - lower factor = quicker drive-off)
            DriveOffDelayFactor = GenerateTraitFromTemperament(
                temperament, traitVariance,
                MinDriveOffDelayFactor, MaxDriveOffDelayFactor,
                correlationDirection: -1)
        };
    }

    /// <summary>
    /// Generate a trait value based on temperament with some variance.
    /// </summary>
    private static float GenerateTraitFromTemperament(
        float temperament,
        float variance,
        float min,
        float max,
        int correlationDirection)
    {
        // Apply correlation direction
        float effectiveTemperament = correlationDirection > 0 ? temperament : 1.0f - temperament;

        // Add per-trait variance
        float traitVariance = (Random.Shared.NextSingle() - 0.5f) * 2.0f * variance;
        float adjustedTemperament = Math.Clamp(effectiveTemperament + traitVariance, 0, 1);

        // Map to trait range
        return min + adjustedTemperament * (max - min);
    }
}
