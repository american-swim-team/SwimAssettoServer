using TrafficAiPlugin.Configuration;

namespace TrafficAiPlugin.Brain;

/// <summary>
/// Information about a leader vehicle for multi-anticipation car-following.
/// </summary>
public readonly struct LeaderInfo
{
    public readonly float Gap;
    public readonly float Speed;
    public readonly float Acceleration;
    public readonly bool IsPlayer;

    public LeaderInfo(float gap, float speed, float acceleration, bool isPlayer)
    {
        Gap = gap;
        Speed = speed;
        Acceleration = acceleration;
        IsPlayer = isPlayer;
    }
}

/// <summary>
/// Static class implementing the Improved Intelligent Driver Model (IIDM) with
/// Constant-Acceleration Heuristic (CAH) and ACC blending for car-following behavior.
///
/// Based on:
/// - IIDM: Fixes "never reaches desired speed" by decoupling free-flow and interaction terms
/// - CAH: Constant-Acceleration Heuristic for smoother approach to stopped/decelerating vehicles
/// - ACC: Adaptive Cruise Control model (Treiber & Kesting) that blends IDM and CAH via coolness factor
/// </summary>
public static class IdmBrain
{
    /// <summary>
    /// Acceleration exponent in IDM formula. Higher = sharper transition to max speed.
    /// </summary>
    private const float Delta = 4.0f;

    /// <summary>
    /// Calculate acceleration using IIDM + CAH + ACC blending.
    /// </summary>
    /// <param name="currentSpeed">Current vehicle speed (m/s)</param>
    /// <param name="desiredSpeed">Desired/target speed (m/s)</param>
    /// <param name="gap">Gap to leader vehicle (m)</param>
    /// <param name="leaderSpeed">Speed of leader vehicle (m/s)</param>
    /// <param name="leaderAcceleration">Estimated leader acceleration (m/s^2)</param>
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
        float leaderAcceleration,
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

        // Calculate IIDM acceleration
        float idmAccel = CalculateIIDM(currentSpeed, desiredSpeed, gap, leaderSpeed, a, b, T, s0);

        // If gap is large (free-flow), skip CAH blending
        float desiredGap = CalculateDesiredGap(currentSpeed, leaderSpeed, a, b, T, s0);
        if (gap > desiredGap * 2 || gap >= float.MaxValue)
        {
            return Math.Clamp(idmAccel, -comfortableDeceleration * 2.0f, maxAcceleration);
        }

        // Calculate CAH acceleration (considers leader acceleration for anticipation)
        float cahAccel = CalculateCAH(currentSpeed, leaderSpeed, gap, leaderAcceleration, a);

        // ACC blending: use coolness factor to blend IDM and CAH
        float coolness = config.CoolnessFactor;
        float accAccel = CalculateACCBlend(idmAccel, cahAccel, coolness, b);

        return Math.Clamp(accAccel, -comfortableDeceleration * 2.0f, maxAcceleration);
    }

    /// <summary>
    /// IIDM (Improved IDM): Decouples free-flow and interaction terms when gap is large,
    /// so traffic can actually reach the desired speed on empty roads.
    /// </summary>
    private static float CalculateIIDM(
        float currentSpeed, float desiredSpeed, float gap, float leaderSpeed,
        float a, float b, float T, float s0)
    {
        // Free-flow term
        float speedRatio = desiredSpeed > 0.01f ? currentSpeed / desiredSpeed : 1.0f;
        float freeFlowAccel = a * (1.0f - MathF.Pow(speedRatio, Delta));

        // Desired gap
        float desiredGap = CalculateDesiredGap(currentSpeed, leaderSpeed, a, b, T, s0);

        // Interaction term
        if (gap < 0.01f) gap = 0.01f;
        float gapRatio = desiredGap / gap;
        float interactionDecel = -a * gapRatio * gapRatio;

        // IIDM key change: when far from leader, use pure free-flow
        if (gap > desiredGap * 2)
            return freeFlowAccel;

        return freeFlowAccel + interactionDecel;
    }

    /// <summary>
    /// Calculate desired minimum gap s* = s0 + max(0, v*T + v*dv/(2*sqrt(a*b)))
    /// </summary>
    private static float CalculateDesiredGap(
        float currentSpeed, float leaderSpeed, float a, float b, float T, float s0)
    {
        float deltaV = currentSpeed - leaderSpeed;
        float sqrtAB = MathF.Sqrt(a * b);
        float dynamicPart = currentSpeed * T + (currentSpeed * deltaV) / (2.0f * sqrtAB);
        return s0 + MathF.Max(0, dynamicPart);
    }

    /// <summary>
    /// Constant-Acceleration Heuristic (CAH).
    /// Assumes leader continues at its current acceleration, producing a smoother
    /// deceleration profile when approaching stopped or decelerating vehicles.
    /// </summary>
    private static float CalculateCAH(
        float currentSpeed, float leaderSpeed, float gap,
        float leaderAcceleration, float maxAcceleration)
    {
        float effectiveGap = MathF.Max(gap, 0.01f);

        // Limit leader acceleration to avoid extreme extrapolation
        float aLeader = Math.Clamp(leaderAcceleration, -maxAcceleration, maxAcceleration);

        if (leaderSpeed * (currentSpeed - leaderSpeed) <= -2.0f * effectiveGap * aLeader)
        {
            // Kinematic equation branch
            float vSquared = currentSpeed * currentSpeed;
            float term = leaderSpeed * leaderSpeed - 2.0f * aLeader * effectiveGap;
            return (vSquared - MathF.Max(0, term)) / (2.0f * effectiveGap);
        }
        else
        {
            float closingSpeed = currentSpeed - leaderSpeed;
            return aLeader - closingSpeed * closingSpeed / (2.0f * effectiveGap);
        }
    }

    /// <summary>
    /// ACC (Adaptive Cruise Control) blending between IDM and CAH.
    /// When IDM is less aggressive than CAH, use IDM (normal following).
    /// When IDM wants harder braking, blend toward CAH using coolness factor.
    /// This prevents harsh braking on approach to stopped vehicles.
    /// </summary>
    private static float CalculateACCBlend(
        float idmAccel, float cahAccel, float coolness, float comfortableDeceleration)
    {
        if (idmAccel >= cahAccel)
        {
            // IDM is less aggressive - use IDM (normal following)
            return idmAccel;
        }
        else
        {
            // IDM wants harder braking than CAH suggests - blend with coolness
            return (1.0f - coolness) * idmAccel
                + coolness * (cahAccel + comfortableDeceleration
                    * MathF.Tanh((idmAccel - cahAccel) / comfortableDeceleration));
        }
    }

    /// <summary>
    /// Calculate multi-anticipative acceleration using weighted sum of IDM responses to multiple leaders.
    /// Based on HDM Model A (Treiber, Kesting, Helbing 2006): a_total = sum(w_j * a_IDM(s_j, v, dv_j))
    /// </summary>
    public static float CalculateMultiAnticipativeAcceleration(
        float currentSpeed, float desiredSpeed,
        ReadOnlySpan<LeaderInfo> leaders, int leaderCount,
        in DriverPersonality personality,
        TrafficAiConfiguration config,
        float maxAcceleration, float comfortableDeceleration)
    {
        if (leaderCount == 0)
        {
            return CalculateAcceleration(currentSpeed, desiredSpeed,
                float.MaxValue, currentSpeed, 0,
                in personality, config, maxAcceleration, comfortableDeceleration);
        }

        if (leaderCount == 1)
        {
            ref readonly var leader = ref leaders[0];
            return CalculateAcceleration(currentSpeed, desiredSpeed,
                leader.Gap, leader.Speed, leader.Acceleration,
                in personality, config, maxAcceleration, comfortableDeceleration);
        }

        // Multi-anticipation: weighted sum of per-leader IDM accelerations
        float decay = config.MultiAnticipationDecay;

        // Compute raw weights and normalize
        Span<float> weights = stackalloc float[leaderCount];
        float weightSum = 0;
        float w = 1.0f;
        for (int j = 0; j < leaderCount; j++)
        {
            weights[j] = w;
            weightSum += w;
            w *= decay;
        }

        float result = 0;
        for (int j = 0; j < leaderCount; j++)
        {
            float normalizedWeight = weights[j] / weightSum;
            ref readonly var leader = ref leaders[j];
            float accel = CalculateAcceleration(currentSpeed, desiredSpeed,
                leader.Gap, leader.Speed, leader.Acceleration,
                in personality, config, maxAcceleration, comfortableDeceleration);
            result += normalizedWeight * accel;
        }

        return result;
    }

    /// <summary>
    /// Calculate desired speed considering road max, cornering limits, and personality.
    /// </summary>
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
    public static float CalculateTimeToCollision(float gap, float relativeVelocity)
    {
        if (relativeVelocity <= 0)
            return float.MaxValue;

        return gap / relativeVelocity;
    }
}
