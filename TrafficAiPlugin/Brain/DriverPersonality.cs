namespace TrafficAiPlugin.Brain;

/// <summary>
/// Readonly struct representing an AI driver's personality traits.
/// These traits affect driving behavior through the IDM brain system.
/// </summary>
public readonly struct DriverPersonality
{
    /// <summary>
    /// Affects following distance and lane change willingness. Range: 0.7-1.3
    /// Higher values = more aggressive driving, shorter following distances.
    /// </summary>
    public float Aggressiveness { get; init; }

    /// <summary>
    /// Affects honk delay and obstacle ignore timeout. Range: 0.6-1.8
    /// Higher values = more patient, longer wait before honking or ignoring obstacles.
    /// </summary>
    public float Patience { get; init; }

    /// <summary>
    /// Multiplier on road speed limit. Range: 0.9-1.1
    /// Higher values = wants to drive faster than the limit.
    /// </summary>
    public float DesiredSpeedFactor { get; init; }

    /// <summary>
    /// Multiplier on IDM time headway. Range: 0.8-1.4
    /// Higher values = prefers larger following distances.
    /// </summary>
    public float FollowingDistanceFactor { get; init; }

    /// <summary>
    /// Comfortable acceleration preference multiplier. Range: 0.8-1.2
    /// Higher values = more aggressive acceleration.
    /// </summary>
    public float AccelerationFactor { get; init; }

    /// <summary>
    /// Braking preference multiplier. Range: 0.85-1.15
    /// Higher values = harder braking when needed.
    /// </summary>
    public float DecelerationFactor { get; init; }

    /// <summary>
    /// Multiplier on reaction time. Range: 0.7-1.3
    /// Higher values = slower reactions (cautious drivers). Lower = faster reactions (aggressive).
    /// </summary>
    public float ReactionTimeFactor { get; init; }

    /// <summary>
    /// Multiplier on drive-off delay and ramp time. Range: 0.6-1.4
    /// Higher values = longer delay before driving off from a stop (cautious).
    /// </summary>
    public float DriveOffDelayFactor { get; init; }

    /// <summary>
    /// Default personality with neutral traits.
    /// </summary>
    public static DriverPersonality Default => new()
    {
        Aggressiveness = 1.0f,
        Patience = 1.0f,
        DesiredSpeedFactor = 1.0f,
        FollowingDistanceFactor = 1.0f,
        AccelerationFactor = 1.0f,
        DecelerationFactor = 1.0f,
        ReactionTimeFactor = 1.0f,
        DriveOffDelayFactor = 1.0f
    };
}
