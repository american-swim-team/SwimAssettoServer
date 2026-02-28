using AssettoServer.Server;
using AssettoServer.Server.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;
using JetBrains.Annotations;
using YamlDotNet.Serialization;

namespace TrafficAiPlugin.Configuration;

#pragma warning disable CS0657
#pragma warning disable CS0618 // Obsolete warnings for backward compat code
[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
public partial class TrafficAiConfiguration : ObservableObject, IValidateConfiguration<TrafficAiConfigurationValidator>
{
    [YamlMember(Description = "Enable usage of /resetcar to teleport the player to the closest spline point. Requires CSP v0.2.3-preview47 or later")]
    public bool EnableCarReset { get; set; } = false;
    [YamlMember(Description = "Automatically assign traffic cars based on the car folder name")]
    public bool AutoAssignTrafficCars { get; init; } = true;

    // --- Simplified spawning parameters (new) ---
    [YamlMember(Description = "Closest distance (meters) an AI can spawn to a player")]
    public float MinSpawnDistanceMeters { get; set; } = 0;
    [YamlMember(Description = "Farthest distance (meters) an AI can spawn from a player")]
    public float MaxSpawnDistanceMeters { get; set; } = 0;
    [YamlMember(Description = "Minimum distance between AI cars (meters)")]
    public float MinTrafficGapMeters { get; set; } = 0;
    [YamlMember(Description = "Maximum distance between AI cars (meters)")]
    public float MaxTrafficGapMeters { get; set; } = 0;
    [YamlMember(Description = "How far from any player before AI despawns (meters)")]
    public float DespawnDistanceMeters { get; set; } = 0;

    // --- Legacy spawning parameters (deprecated, kept for backward compat) ---
    [Obsolete("Use DespawnDistanceMeters instead")]
    [YamlMember(Description = "Radius around a player in which AI cars won't despawn", DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public float PlayerRadiusMeters { get; set; } = 0;
    [YamlMember(Description = "Offset the player radius in direction of the velocity of the player so AI cars will despawn earlier behind a player")]
    public float PlayerPositionOffsetMeters { get; set; } = 100.0f;
    [YamlMember(Description = "AFK timeout for players. Players who are AFK longer than this won't spawn AI cars")]
    public long PlayerAfkTimeoutSeconds { get; set; } = 10;
    [YamlMember(Description = "Maximum distance to the AI spline for a player to spawn AI cars. This helps with parts of the map without traffic so AI cars won't spawn far away from players")]
    public float MaxPlayerDistanceToAiSplineMeters { get; set; } = 7;
    [Obsolete("Use MinSpawnDistanceMeters instead")]
    [YamlMember(Description = "Minimum amount of spline points in front of a player where AI cars will spawn", DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public int MinSpawnDistancePoints { get; set; } = 0;
    [Obsolete("Use MaxSpawnDistanceMeters instead")]
    [YamlMember(Description = "Maximum amount of spline points in front of a player where AI cars will spawn", DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public int MaxSpawnDistancePoints { get; set; } = 0;
    [Obsolete("Use MinTrafficGapMeters instead")]
    [YamlMember(Description = "Minimum distance between AI cars", DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public int MinAiSafetyDistanceMeters { get; set; } = 0;
    [Obsolete("Use MaxTrafficGapMeters instead")]
    [YamlMember(Description = "Maximum distance between AI cars", DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public int MaxAiSafetyDistanceMeters { get; set; } = 0;
    [YamlMember(Description = "Minimum spawn distance for AI states of the same car slot. If you set this too low you risk AI states despawning or AI states becoming invisible for some players when multiple states are close together")]
    public float StateSpawnDistanceMeters { get; set; } = 1000;
    [YamlMember(Description = "Minimum distance between AI states of the same car slot. If states get closer than this one of them will be forced to despawn")]
    public float MinStateDistanceMeters { get; set; } = 200;
    [YamlMember(Description = "Minimum spawn distance to players")]
    public float SpawnSafetyDistanceToPlayerMeters { get; set; } = 150;
    [YamlMember(Description = "Minimum time in which a newly spawned AI car cannot despawn")]
    public int MinSpawnProtectionTimeSeconds { get; set; } = 4;
    [YamlMember(Description = "Maximum time in which a newly spawned AI car cannot despawn")]
    public int MaxSpawnProtectionTimeSeconds { get; set; } = 8;
    [YamlMember(Description = "Minimum time an AI car will stop/slow down after a collision")]
    public int MinCollisionStopTimeSeconds { get; set; } = 1;
    [YamlMember(Description = "Maximum time an AI car will stop/slow down after a collision")]
    public int MaxCollisionStopTimeSeconds { get; set; } = 3;
    [YamlMember(Description = "Default maximum AI speed. This is not an absolute maximum and can be overridden with RightLaneOffsetKph and MaxSpeedVariationPercent")]
    public float MaxSpeedKph { get; set; } = 80;
    [YamlMember(Description = "Speed offset for right lanes. Will be added to MaxSpeedKph")]
    public float RightLaneOffsetKph { get; set; } = 10;
    [YamlMember(Description = "Maximum speed variation")]
    public float MaxSpeedVariationPercent { get; set; } = 0.15f;

    [ObservableProperty]
    [property: YamlMember(Description = "Default AI car deceleration for obstacle/collision detection (m/s^2)")]
    private float _defaultDeceleration = 8.5f;

    [ObservableProperty]
    [property: YamlMember(Description = "Default AI car acceleration for obstacle/collision detection (m/s^2)")]
    private float _defaultAcceleration = 2.5f;

    [ObservableProperty]
    [property: YamlMember(Description = "Maximum AI car target count for AI slot overbooking. This is not an absolute maximum and might be slightly higher (0 = auto/best)", DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    private int _maxAiTargetCount;

    [ObservableProperty]
    [property: YamlMember(Description = "Number of AI cars per player the server will try to keep (0 = auto/best)", DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    private int _aiPerPlayerTargetCount;

    [YamlMember(Description = "Soft player limit, the server will stop accepting new players when this many players are reached. Use this to ensure a minimum amount of AI cars. 0 to disable.")]
    public int MaxPlayerCount { get; set; } = 0;
    [YamlMember(Description = "Hide AI car nametags and make them invisible on the minimap. Broken on CSP versions < 0.1.78")]
    public bool HideAiCars { get; set; } = true;

    [ObservableProperty]
    [property: YamlMember(Description = "AI spline height offset. Use this if the AI spline is too close to the ground")]
    private float _splineHeightOffsetMeters = 0;

    [YamlMember(Description = "Lane width for adjacent lane detection")]
    public float LaneWidthMeters { get; init; } = 3.0f;
    [YamlMember(Description = "Enable two way traffic. This will allow AI cars to spawn in lanes with the opposite direction of travel to the player.")]
    public bool TwoWayTraffic { get; set; } = false;
    [YamlMember(Description = "Enable traffic spawning if the player is driving the wrong way. Only takes effect when TwoWayTraffic is set to false.")]
    public bool WrongWayTraffic { get; set; } = true;

    [YamlMember(Description = "Enable AI lane changing behavior")]
    public bool EnableLaneChanging { get; set; } = true;

    [YamlMember(Description = "Minimum distance for lane change maneuver (meters)")]
    public float LaneChangeMinDistanceMeters { get; set; } = 20f;

    [YamlMember(Description = "Maximum distance for lane change maneuver (meters)")]
    public float LaneChangeMaxDistanceMeters { get; set; } = 50f;

    [YamlMember(Description = "Speed difference threshold to trigger overtake (km/h)")]
    public float LaneChangeSpeedThresholdKph { get; set; } = 10f;

    [YamlMember(Description = "Cooldown between lane change attempts (seconds)")]
    public float LaneChangeCooldownSeconds { get; set; } = 8f;

    [YamlMember(Description = "Multiplier for max distance when checking ahead in target lane")]
    public float LaneChangeLookAheadMultiplier { get; set; } = 2.0f;

    [YamlMember(Description = "Multiplier for max distance when checking behind in target lane")]
    public float LaneChangeLookBehindMultiplier { get; set; } = 1.0f;

    [YamlMember(Description = "Time horizon in seconds for player position prediction during lane change safety checks")]
    public float LaneChangeTimeHorizonSeconds { get; set; } = 2.0f;

    [YamlMember(Description = "Check behind for approaching players before lane change (prevents merging into faster players)")]
    public bool LaneChangeCheckBehindForPlayers { get; set; } = true;

    [YamlMember(Description = "How far behind to check for approaching players during lane change (meters)")]
    public float LaneChangePlayerLookBehindMeters { get; set; } = 50f;

    [YamlMember(Description = "Distance after lane change to keep indicator on (meters)")]
    public float LaneChangeIndicatorClearDistanceMeters { get; set; } = 20.0f;

    [YamlMember(Description = "Minimum speed factor for lane change distance calculation")]
    public float LaneChangeSpeedFactorMin { get; set; } = 0.5f;

    [YamlMember(Description = "Maximum speed factor for lane change distance calculation")]
    public float LaneChangeSpeedFactorMax { get; set; } = 1.5f;

    // IDM Brain Configuration
    [YamlMember(Description = "IDM base time headway in seconds (target following time gap)")]
    public float IdmBaseTimeHeadwaySeconds { get; set; } = 1.5f;

    [YamlMember(Description = "IDM minimum jam gap in meters (minimum distance when stopped)")]
    public float IdmMinGapMeters { get; set; } = 2.0f;

    [YamlMember(Description = "Coolness factor for ACC model (0 = pure IDM/reactive, 1 = maximum anticipatory). Blends IDM with Constant-Acceleration Heuristic for smoother approach to stopped vehicles")]
    public float CoolnessFactor { get; set; } = 0.99f;

    // Personality configuration (simplified)
    [YamlMember(Description = "Personality variation (0 = identical drivers, 1 = maximum variety)")]
    public float PersonalityVariety { get; set; } = 0.3f;

    [YamlMember(Description = "Personality bias (-1 = all passive/cautious, 0 = neutral, +1 = all aggressive)")]
    public float PersonalityBias { get; set; } = 0.0f;

    [YamlMember(Description = "Overtake desire threshold - lane change triggers when desire exceeds this value (0-1)")]
    public float OvertakeDesireThreshold { get; set; } = 0.5f;

    [ObservableProperty]
    [property: YamlMember(Description = "AI cornering speed factor. Lower = AI cars will drive slower around corners.")]
    private float _corneringSpeedFactor = 0.65f;

    [ObservableProperty]
    [property: YamlMember(Description = "AI cornering brake distance factor. Lower = AI cars will brake later for corners.")]
    private float _corneringBrakeDistanceFactor = 3;

    [ObservableProperty]
    [property: YamlMember(Description = "AI cornering brake force factor. This is multiplied with DefaultDeceleration. Lower = AI cars will brake less hard for corners.")]
    private float _corneringBrakeForceFactor = 0.5f;

    [YamlMember(Description = "Name prefix for AI cars. Names will be in the form of '<NamePrefix> <SessionId>'")]
    public string NamePrefix { get; init; } = "Traffic";
    [YamlMember(Description = "Ignore obstacles for some time if the AI car is stopped for longer than x seconds")]
    public int IgnoreObstaclesAfterSeconds { get; set; } = 10;
    [YamlMember(Description = "Duration of 'ignore obstacles' mode after timeout (seconds)")]
    public int IgnoreModeDurationSeconds { get; set; } = 10;
    [YamlMember(Description = "Extra lookahead buffer beyond braking distance (meters)")]
    public float LookaheadBufferMeters { get; set; } = 20f;
    [YamlMember(Description = "Angle range for detecting player obstacles behind AI (degrees from 180, so 14 means 166-194 degrees)")]
    public float PlayerDetectionAngleRange { get; set; } = 14f;
    [YamlMember(Description = "Height difference threshold for player obstacle detection (meters)")]
    public float PlayerDetectionHeightThreshold { get; set; } = 1.5f;

    [ObservableProperty]
    [property: YamlMember(Description = "Apply scale to some traffic density related settings. Increasing this DOES NOT magically increase your traffic density, it is dependent on your other settings. Values higher than 1 not recommended.", DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    private float _trafficDensity = 1.0f;

    [YamlMember(Description = "Dynamic (hourly) traffic density. List must have exactly 24 entries in the format [0.2, 0.5, 1, 0.7, ...]")]
    public List<float>? HourlyTrafficDensity { get; set; }

    [ObservableProperty]
    [property: YamlMember(Description = "Tyre diameter of AI cars in meters, shouldn't have to be changed unless some cars are creating lots of smoke.")]
    private float _tyreDiameterMeters = 0.65f;

    [YamlMember(Description = "Apply some smoothing to AI spline camber")]
    public bool SmoothCamber { get; init; } = true;
    [YamlMember(Description = "Show debug overlay for AI cars", DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool Debug { get; set; } = false;
    [YamlMember(Description = "Update interval for AI spawn point finder")]
    public int AiBehaviorUpdateIntervalHz { get; set; } = 2;
    [YamlMember(Description = "Enable AI car headlights during the day")]
    public bool EnableDaytimeLights { get; set; } = false;
    [YamlMember(Description = "AI cars inside these areas will ignore all player obstacles")]
    public List<Sphere>? IgnorePlayerObstacleSpheres { get; set; }
    [YamlMember(Description = "Override some settings for newly spawned cars based on the number of lanes")]
    public Dictionary<int, LaneCountSpecificOverrides> LaneCountSpecificOverrides { get; set; } = new();

    [YamlMember(Description = "Override some settings for specific car models")]
    public List<CarSpecificOverrides> CarSpecificOverrides { get; init; } = [];

    // --- Player clustering (Change 2) ---
    [YamlMember(Description = "Players within this distance are treated as one group for spawning (meters)")]
    public float PlayerClusterRadiusMeters { get; set; } = 150.0f;

    [YamlMember(Description = "Diminishing factor for additional players in a cluster (0-1). Controls how many extra AI spawn near convoys")]
    public float ClusterDiminishingFactor { get; set; } = 0.25f;

    // --- Multi-vehicle anticipation ---
    [YamlMember(Description = "Number of vehicles ahead to consider for car-following (1 = single-leader, 2-3 = multi-anticipation)")]
    public int MultiAnticipationCount { get; set; } = 3;

    [YamlMember(Description = "Geometric weight decay for multi-anticipation (leader2 = decay * leader1's weight)")]
    public float MultiAnticipationDecay { get; set; } = 0.5f;

    // --- Drive-off delay ---
    [YamlMember(Description = "Minimum delay before accelerating from a stop (seconds)")]
    public float DriveOffDelayMinSeconds { get; set; } = 0.8f;

    [YamlMember(Description = "Maximum delay before accelerating from a stop (seconds)")]
    public float DriveOffDelayMaxSeconds { get; set; } = 1.8f;

    [YamlMember(Description = "Time to ramp from zero to full acceleration after drive-off delay (seconds)")]
    public float DriveOffRampSeconds { get; set; } = 1.5f;

    // --- Reaction time (Change 4) ---
    [YamlMember(Description = "Minimum perception delay before AI reacts to a new obstacle (seconds)")]
    public float ReactionTimeMinSeconds { get; set; } = 0.3f;

    [YamlMember(Description = "Maximum perception delay before AI reacts to a new obstacle (seconds)")]
    public float ReactionTimeMaxSeconds { get; set; } = 1.0f;

    // --- Estimation noise (Change 5E) ---
    [YamlMember(Description = "Noise on perceived gap distance (0.1 = +/-10%)")]
    public float GapEstimationError { get; set; } = 0.1f;

    [YamlMember(Description = "Noise on perceived leader speed (0.02 = +/-2%)")]
    public float SpeedEstimationError { get; set; } = 0.02f;

    // --- Computed properties ---
    [YamlIgnore] public float EffectivePlayerRadiusMeters => DespawnDistanceMeters > 0 ? DespawnDistanceMeters : (PlayerRadiusMeters > 0 ? PlayerRadiusMeters : 200.0f);
    [YamlIgnore] public float PlayerRadiusSquared => EffectivePlayerRadiusMeters * EffectivePlayerRadiusMeters;
    [YamlIgnore] public float PlayerAfkTimeoutMilliseconds => PlayerAfkTimeoutSeconds * 1000;
    [YamlIgnore] public float MaxPlayerDistanceToAiSplineSquared => MaxPlayerDistanceToAiSplineMeters * MaxPlayerDistanceToAiSplineMeters;
    [YamlIgnore] public int EffectiveMinAiSafetyDistanceMeters => MinTrafficGapMeters > 0 ? (int)MinTrafficGapMeters : (MinAiSafetyDistanceMeters > 0 ? MinAiSafetyDistanceMeters : 20);
    [YamlIgnore] public int EffectiveMaxAiSafetyDistanceMeters => MaxTrafficGapMeters > 0 ? (int)MaxTrafficGapMeters : (MaxAiSafetyDistanceMeters > 0 ? MaxAiSafetyDistanceMeters : 70);
    [YamlIgnore] public int MinAiSafetyDistanceSquared => EffectiveMinAiSafetyDistanceMeters * EffectiveMinAiSafetyDistanceMeters;
    [YamlIgnore] public int MaxAiSafetyDistanceSquared => EffectiveMaxAiSafetyDistanceMeters * EffectiveMaxAiSafetyDistanceMeters;
    [YamlIgnore] public float StateSpawnDistanceSquared => StateSpawnDistanceMeters * StateSpawnDistanceMeters;
    [YamlIgnore] public float MinStateDistanceSquared => MinStateDistanceMeters * MinStateDistanceMeters;
    [YamlIgnore] public float SpawnSafetyDistanceToPlayerSquared => SpawnSafetyDistanceToPlayerMeters * SpawnSafetyDistanceToPlayerMeters;
    [YamlIgnore] public int MinSpawnProtectionTimeMilliseconds => MinSpawnProtectionTimeSeconds * 1000;
    [YamlIgnore] public int MaxSpawnProtectionTimeMilliseconds => MaxSpawnProtectionTimeSeconds * 1000;
    [YamlIgnore] public int MinCollisionStopTimeMilliseconds => MinCollisionStopTimeSeconds * 1000;
    [YamlIgnore] public int MaxCollisionStopTimeMilliseconds => MaxCollisionStopTimeSeconds * 1000;
    [YamlIgnore] public float MaxSpeedMs => MaxSpeedKph / 3.6f;
    [YamlIgnore] public float RightLaneOffsetMs => RightLaneOffsetKph / 3.6f;
    [YamlIgnore] public int IgnoreObstaclesAfterMilliseconds => IgnoreObstaclesAfterSeconds * 1000;
    [YamlIgnore] public int AiBehaviorUpdateIntervalMilliseconds => 1000 / AiBehaviorUpdateIntervalHz;
    [YamlIgnore] public float LaneChangeSpeedThresholdMs => LaneChangeSpeedThresholdKph / 3.6f;
    [YamlIgnore] public int LaneChangeCooldownMilliseconds => (int)(LaneChangeCooldownSeconds * 1000);
    [YamlIgnore] public int IgnoreModeDurationMilliseconds => IgnoreModeDurationSeconds * 1000;
    [YamlIgnore] public float ReactionTimeMinMs => ReactionTimeMinSeconds * 1000.0f;
    [YamlIgnore] public float ReactionTimeMaxMs => ReactionTimeMaxSeconds * 1000.0f;

    // Effective spawn distance points (computed from meters or legacy values)
    [YamlIgnore] public int EffectiveMinSpawnDistancePoints { get; internal set; } = 100;
    [YamlIgnore] public int EffectiveMaxSpawnDistancePoints { get; internal set; } = 400;

    internal void ApplyConfigurationFixes(ACServerConfiguration serverConfiguration)
    {
        if (AiPerPlayerTargetCount == 0)
        {
            AiPerPlayerTargetCount = serverConfiguration.EntryList.Cars.Count(c => c.AiMode != AiMode.None);
        }

        if (MaxAiTargetCount == 0)
        {
            MaxAiTargetCount = serverConfiguration.EntryList.Cars.Count(c => c.AiMode != AiMode.Fixed) * AiPerPlayerTargetCount;
        }

        // Migrate new simplified params to legacy fields for backward compat
        if (DespawnDistanceMeters > 0 && PlayerRadiusMeters == 0)
        {
            PlayerRadiusMeters = DespawnDistanceMeters;
        }
        else if (PlayerRadiusMeters == 0)
        {
            // Neither set - use defaults
            PlayerRadiusMeters = 200.0f;
        }

        if (MinTrafficGapMeters > 0 && MinAiSafetyDistanceMeters == 0)
        {
            MinAiSafetyDistanceMeters = (int)MinTrafficGapMeters;
        }
        else if (MinAiSafetyDistanceMeters == 0)
        {
            MinAiSafetyDistanceMeters = 20;
        }

        if (MaxTrafficGapMeters > 0 && MaxAiSafetyDistanceMeters == 0)
        {
            MaxAiSafetyDistanceMeters = (int)MaxTrafficGapMeters;
        }
        else if (MaxAiSafetyDistanceMeters == 0)
        {
            MaxAiSafetyDistanceMeters = 70;
        }

        // Legacy point-based spawn distances take priority if explicitly set
        if (MinSpawnDistancePoints > 0)
        {
            EffectiveMinSpawnDistancePoints = MinSpawnDistancePoints;
        }
        else if (MinSpawnDistanceMeters > 0)
        {
            // Will be converted to points at spawn time via AiSpline.MetersToPoints
            // Store a rough estimate here (assume ~1m per point as fallback)
            EffectiveMinSpawnDistancePoints = (int)MinSpawnDistanceMeters;
        }

        if (MaxSpawnDistancePoints > 0)
        {
            EffectiveMaxSpawnDistancePoints = MaxSpawnDistancePoints;
        }
        else if (MaxSpawnDistanceMeters > 0)
        {
            EffectiveMaxSpawnDistancePoints = (int)MaxSpawnDistanceMeters;
        }

        // Compute PlayerPositionOffset from DespawnDistance if using new params
        if (DespawnDistanceMeters > 0 && PlayerPositionOffsetMeters == 100.0f)
        {
            PlayerPositionOffsetMeters = DespawnDistanceMeters * 0.5f;
        }
    }
}
