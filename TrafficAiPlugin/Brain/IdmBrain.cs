using TrafficAiPlugin.Configuration;

namespace TrafficAiPlugin.Brain;

/// <summary>
/// Static class implementing the Intelligent Driver Model (IDM) for car-following behavior.
///
/// IDM provides smooth, physics-based acceleration calculations based on:
/// - Free-flow term: Accelerate toward desired speed when road is clear
/// - Interaction term: Decelerate based on gap to leader and closing rate
/// </summary>
public static class IdmBrain
{
    /// <summary>
    /// Acceleration exponent in IDM formula. Higher = sharper transition to max speed.
    /// </summary>
    private const float Delta = 4.0f;

    /// <summary>
    /// Calculate IDM acceleration.
    ///
    /// Formula: a = a_max * [1 - (v/v0)^delta - (s*/s)^2]
    /// Where s* = s0 + max(0, v*T + v*dv/(2*sqrt(a*b)))
    /// </summary>
    /// <param name="currentSpeed">Current vehicle speed (m/s)</param>
    /// <param name="desiredSpeed">Desired/target speed (m/s)</param>
    /// <param name="gap">Gap to leader vehicle (m)</param>
    /// <param name="leaderSpeed">Speed of leader vehicle (m/s)</param>
    /// <param name="personality">Driver personality traits</param>
    /// <param name="config">Traffic AI configuration</param>
    /// <param name="maxAcceleration">Maximum acceleration (m/s^2)</param>
    /// <param name="comfortableDeceleration">Comfortable deceleration (m/s^2)</param>
    /// <returns>Acceleration value (m/s^2), positive or negative</returns>
    public static float CalculateAcceleration(
        float currentSpeed,
        float desiredSpeed,
        float gap,
        float leaderSpeed,
        in DriverPersonality personality,
        TrafficAiConfiguration config,
        float maxAcceleration,
        float comfortableDeceleration)
    {
        // Apply personality to base parameters
        float a = maxAcceleration * personality.AccelerationFactor;
        float b = comfortableDeceleration * personality.DecelerationFactor;
        float T = config.IdmBaseTimeHeadwaySeconds * personality.FollowingDistanceFactor;
        float s0 = config.IdmMinGapMeters;

        // Velocity difference (positive when closing on leader)
        float deltaV = currentSpeed - leaderSpeed;

        // Calculate desired minimum gap (s*)
        float sqrtAB = MathF.Sqrt(a * b);
        float dynamicPart = currentSpeed * T + (currentSpeed * deltaV) / (2.0f * sqrtAB);
        float desiredGap = s0 + MathF.Max(0, dynamicPart);

        // Free-flow term: tendency to accelerate toward desired speed
        float speedRatio = desiredSpeed > 0.01f ? currentSpeed / desiredSpeed : 1.0f;
        float freeFlowTerm = 1.0f - MathF.Pow(speedRatio, Delta);

        // Interaction term: tendency to decelerate based on gap
        float interactionTerm = 0;
        if (gap > 0.01f)
        {
            float gapRatio = desiredGap / gap;
            interactionTerm = gapRatio * gapRatio;
        }

        // Final acceleration
        float acceleration = a * (freeFlowTerm - interactionTerm);

        // Clamp to reasonable bounds
        return Math.Clamp(acceleration, -comfortableDeceleration * 2.0f, maxAcceleration);
    }

    /// <summary>
    /// Calculate desired speed considering road max, cornering limits, and personality.
    /// </summary>
    /// <param name="roadMaxSpeed">Maximum speed for the road (m/s)</param>
    /// <param name="corneringMaxSpeed">Maximum safe cornering speed (m/s)</param>
    /// <param name="personality">Driver personality traits</param>
    /// <returns>Desired speed (m/s)</returns>
    public static float CalculateDesiredSpeed(
        float roadMaxSpeed,
        float corneringMaxSpeed,
        in DriverPersonality personality)
    {
        float baseSpeed = MathF.Min(roadMaxSpeed, corneringMaxSpeed);
        return baseSpeed * personality.DesiredSpeedFactor;
    }

    /// <summary>
    /// Calculate overtake desire based on current driving conditions.
    /// Returns a value from 0 (no desire) to 1 (strong desire) indicating
    /// how much the driver wants to change lanes to overtake.
    /// </summary>
    /// <param name="currentSpeed">Current vehicle speed (m/s)</param>
    /// <param name="desiredSpeed">Desired/target speed (m/s)</param>
    /// <param name="gap">Gap to leader vehicle (m)</param>
    /// <param name="leaderSpeed">Speed of leader vehicle (m/s)</param>
    /// <param name="personality">Driver personality traits</param>
    /// <param name="config">Traffic AI configuration</param>
    /// <returns>Overtake desire from 0 to 1</returns>
    public static float CalculateOvertakeDesire(
        float currentSpeed,
        float desiredSpeed,
        float gap,
        float leaderSpeed,
        in DriverPersonality personality,
        TrafficAiConfiguration config)
    {
        // No leader or leader very far away - no desire to overtake
        if (gap > config.LaneChangeMaxDistanceMeters * 3.0f || gap < 0)
            return 0;

        // Calculate frustration based on speed deficit
        float speedDeficit = desiredSpeed - currentSpeed;
        if (speedDeficit <= 0)
            return 0; // Already at or above desired speed

        // Normalize speed deficit (assume max frustration at 20% speed deficit)
        float speedFrustration = Math.Clamp(speedDeficit / (desiredSpeed * 0.2f), 0, 1);

        // Calculate frustration based on following too closely
        float idealGap = config.IdmBaseTimeHeadwaySeconds * personality.FollowingDistanceFactor * currentSpeed
                        + config.IdmMinGapMeters;
        float gapFrustration = gap < idealGap ? Math.Clamp(1.0f - (gap / idealGap), 0, 1) : 0;

        // Leader going slower than us adds to desire
        float closingSpeed = currentSpeed - leaderSpeed;
        float closingFrustration = closingSpeed > 0 ? Math.Clamp(closingSpeed / 5.0f, 0, 1) : 0;

        // Combine frustrations (weighted average)
        float baseFrustration = speedFrustration * 0.4f + gapFrustration * 0.35f + closingFrustration * 0.25f;

        // Aggressive drivers have higher overtake desire
        float desire = baseFrustration * personality.Aggressiveness;

        return Math.Clamp(desire, 0, 1);
    }

    /// <summary>
    /// Calculate time until collision assuming constant speeds.
    /// </summary>
    /// <param name="gap">Gap to leader (m)</param>
    /// <param name="relativeVelocity">Relative velocity (positive = closing)</param>
    /// <returns>Time to collision in seconds, or float.MaxValue if not closing</returns>
    public static float CalculateTimeToCollision(float gap, float relativeVelocity)
    {
        if (relativeVelocity <= 0)
            return float.MaxValue;

        return gap / relativeVelocity;
    }
}
