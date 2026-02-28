using System.Drawing;
using System.Numerics;
using AssettoServer.Server;
using AssettoServer.Server.Configuration;
using AssettoServer.Server.Weather;
using AssettoServer.Shared.Model;
using AssettoServer.Shared.Network.Packets.Outgoing;
using AssettoServer.Shared.Utils;
using AssettoServer.Utils;
using JPBotelho;
using Serilog;
using SunCalcNet.Model;
using TrafficAiPlugin.Brain;
using TrafficAiPlugin.Configuration;
using TrafficAiPlugin.Shared;
using TrafficAiPlugin.Shared.Splines;
using TrafficAiPlugin.Splines;

namespace TrafficAiPlugin;

public class AiState : IAiState, IDisposable
{
    public byte SessionId => EntryCarAi.EntryCar.SessionId;
    public CarStatus Status { get; } = new();
    public bool Initialized { get; private set; }

    public int CurrentSplinePointId
    {
        get => _currentSplinePointId;
        private set
        {
            _spline.SlowestAiStates.Enter(value, this);
            _spline.SlowestAiStates.Leave(_currentSplinePointId, this);
            _currentSplinePointId = value;
        }
    }

    private int _currentSplinePointId;
    
    public long SpawnProtectionEnds { get; set; }
    public float SafetyDistanceSquared { get; set; } = 20 * 20;
    public float Acceleration { get; set; }
    public float CurrentSpeed { get; private set; }
    public float TargetSpeed { get; private set; }
    public float InitialMaxSpeed { get; private set; }
    public float MaxSpeed { get; private set; }
    public Color Color { get; private set; }
    public byte SpawnCounter { get; private set; }
    public float ClosestAiObstacleDistance { get; private set; }
    public EntryCarTrafficAi EntryCarAi { get; }

    // Expose lane change state for collision detection by other cars
    public bool IsCurrentlyLaneChanging => _laneChange.IsChangingLane;
    public int LaneChangeSourcePointId => _laneChange.SourcePointId;
    public int LaneChangeTargetPointId => _laneChange.TargetPointId;
    public bool IsLaneChangeOvertake => _laneChange.IsOvertake;

    private const float WalkingSpeed = 10 / 3.6f;

    private Vector3 _startTangent;
    private Vector3 _endTangent;

    private float _currentVecLength;
    private float _currentVecProgress;
    private long _lastTick;
    private bool _stoppedForObstacle;
    private long _stoppedForObstacleSince;
    private long _ignoreObstaclesUntil;
    private long _stoppedForCollisionUntil;
    private long _obstacleHonkStart;
    private long _obstacleHonkEnd;
    private CarStatusFlags _indicator = 0;
    private int _nextJunctionId;
    private bool _junctionPassed;
    private bool _junctionUnsafe;
    private float _endIndicatorDistance;
    private float _minObstacleDistance;
    private double _randomTwilight;

    // Leader acceleration tracking for CAH model (Change 3/5)
    private float _leaderAcceleration;
    private float _lastLeaderSpeed;

    // Jerk limiting (Change 6B)
    private float _previousAcceleration;

    // Perception delay for cut-ins (Change 4)
    private float _perceivedObstacleDistance = float.MaxValue;
    private float _perceivedObstacleSpeed;
    private long _newObstacleDetectedAt;
    private bool _obstaclePerceived = true;
    private float _reactionTimeMs;

    // Estimation noise (Change 5E)
    private float _gapNoiseState;
    private float _speedNoiseState;

    // Drive-off delay
    private bool _waitingToDriveOff;
    private long _driveOffAt;
    private float _driveOffRampTime;
    private float _driveOffElapsed;

    // Lane change state
    private readonly LaneChangeState _laneChange = new();
    private long _lastLaneChangeAttempt;
    private int _laneChangeSourcePointId = -1; // For dual SlowestAiStates tracking during lane change
    private int _laneChangeTargetRegistrationPointId = -1; // Track where we registered in target lane (adjacent point)

    // IDM brain state
    private DriverPersonality _personality;
    private float _desiredSpeed;
    private float _lastOvertakeDesire;

    private readonly ACServerConfiguration _serverConfiguration;
    private readonly TrafficAiConfiguration _configuration;
    private readonly SessionManager _sessionManager;
    private readonly EntryCarManager _entryCarManager;
    private readonly WeatherManager _weatherManager;
    private readonly AiSpline _spline;
    private readonly TrafficAi _trafficAi;
    private readonly JunctionEvaluator _junctionEvaluator;
    private readonly PersonalityFactory _personalityFactory;

    private static readonly List<Color> CarColors =
    [
        Color.FromArgb(13, 17, 22),
        Color.FromArgb(19, 24, 31),
        Color.FromArgb(28, 29, 33),
        Color.FromArgb(12, 13, 24),
        Color.FromArgb(11, 20, 33),
        Color.FromArgb(151, 154, 151),
        Color.FromArgb(153, 157, 160),
        Color.FromArgb(194, 196, 198),
        Color.FromArgb(234, 234, 234),
        Color.FromArgb(255, 255, 255),
        Color.FromArgb(182, 17, 27),
        Color.FromArgb(218, 25, 24),
        Color.FromArgb(73, 17, 29),
        Color.FromArgb(35, 49, 85),
        Color.FromArgb(28, 53, 81),
        Color.FromArgb(37, 58, 167),
        Color.FromArgb(21, 92, 45),
        Color.FromArgb(18, 46, 43)
    ];

    public AiState(EntryCarTrafficAi entryCarAi,
        SessionManager sessionManager,
        WeatherManager weatherManager,
        ACServerConfiguration serverConfiguration,
        TrafficAiConfiguration configuration,
        EntryCarManager entryCarManager,
        AiSpline spline,
        TrafficAi trafficAi,
        PersonalityFactory personalityFactory)
    {
        EntryCarAi = entryCarAi;
        _sessionManager = sessionManager;
        _weatherManager = weatherManager;
        _serverConfiguration = serverConfiguration;
        _configuration = configuration;
        _entryCarManager = entryCarManager;
        _spline = spline;
        _trafficAi = trafficAi;
        _junctionEvaluator = new JunctionEvaluator(spline);
        _personalityFactory = personalityFactory;

        _lastTick = _sessionManager.ServerTimeMilliseconds;
    }

    ~AiState()
    {
        Despawn();
    }
    
    public void Dispose()
    {
        Despawn();
        GC.SuppressFinalize(this);
    }

    public void Despawn()
    {
        Initialized = false;
        _spline.SlowestAiStates.Leave(CurrentSplinePointId, this);

        // Clean up lane change source point tracking if mid-lane-change
        if (_laneChangeSourcePointId >= 0)
        {
            _spline.SlowestAiStates.Leave(_laneChangeSourcePointId, this);
            _laneChangeSourcePointId = -1;
        }

        // Clean up target lane registration point if mid-lane-change
        if (_laneChangeTargetRegistrationPointId >= 0)
        {
            _spline.SlowestAiStates.Leave(_laneChangeTargetRegistrationPointId, this);
            _laneChangeTargetRegistrationPointId = -1;
        }

        if (_laneChange.IsChangingLane)
        {
            _laneChange.Abort();
        }
    }

    private void SetRandomSpeed()
    {
        float variation = _configuration.MaxSpeedMs * _configuration.MaxSpeedVariationPercent;

        float fastLaneOffset = 0;
        if (_spline.Points[CurrentSplinePointId].LeftId >= 0)
        {
            fastLaneOffset = _configuration.RightLaneOffsetMs;
        }
        InitialMaxSpeed = _configuration.MaxSpeedMs + fastLaneOffset - (variation / 2) + (float)Random.Shared.NextDouble() * variation;
        CurrentSpeed = InitialMaxSpeed;
        TargetSpeed = InitialMaxSpeed;
        MaxSpeed = InitialMaxSpeed;
    }

    private void SetRandomColor()
    {
        Color = CarColors[Random.Shared.Next(CarColors.Count)];
    }

    public void Teleport(int pointId)
    {
        _junctionEvaluator.Clear();
        CurrentSplinePointId = pointId;
        if (!_junctionEvaluator.TryNext(CurrentSplinePointId, out var nextPointId))
            throw new InvalidOperationException($"Cannot get next spline point for {CurrentSplinePointId}");
        _currentVecLength = (_spline.Points[nextPointId].Position - _spline.Points[CurrentSplinePointId].Position).Length();
        _currentVecProgress = 0;
            
        CalculateTangents();

        SetRandomSpeed();

        // Only set random color on first spawn to avoid color changes on respawn
        if (SpawnCounter == 0)
        {
            SetRandomColor();
        }

        // Assign personality at spawn
        _personality = _personalityFactory.Create();
        _desiredSpeed = InitialMaxSpeed * _personality.DesiredSpeedFactor;
        _lastOvertakeDesire = 0;

        var minDist = _configuration.MinAiSafetyDistanceSquared;
        var maxDist = _configuration.MaxAiSafetyDistanceSquared;
        if (_configuration.LaneCountSpecificOverrides.TryGetValue(_spline.GetLanes(CurrentSplinePointId).Length, out var overrides))
        {
            minDist = overrides.MinAiSafetyDistanceSquared;
            maxDist = overrides.MaxAiSafetyDistanceSquared;
        }
        
        if (EntryCarAi.MinAiSafetyDistanceMetersSquared.HasValue)
            minDist = EntryCarAi.MinAiSafetyDistanceMetersSquared.Value;
        if (EntryCarAi.MaxAiSafetyDistanceMetersSquared.HasValue)
            maxDist = EntryCarAi.MaxAiSafetyDistanceMetersSquared.Value;

        SpawnProtectionEnds = _sessionManager.ServerTimeMilliseconds + Random.Shared.Next(EntryCarAi.AiMinSpawnProtectionTimeMilliseconds, EntryCarAi.AiMaxSpawnProtectionTimeMilliseconds);
        SafetyDistanceSquared = Random.Shared.Next((int)Math.Round(minDist * (1.0f / _configuration.TrafficDensity)),
            (int)Math.Round(maxDist * (1.0f / _configuration.TrafficDensity)));
        _stoppedForCollisionUntil = 0;
        _ignoreObstaclesUntil = 0;
        _obstacleHonkEnd = 0;
        _obstacleHonkStart = 0;
        _indicator = 0;
        _randomTwilight = Random.Shared.NextSingle(0, 12) * Math.PI / 180.0;
        _nextJunctionId = -1;
        _junctionPassed = false;
        _endIndicatorDistance = 0;
        _lastTick = _sessionManager.ServerTimeMilliseconds;
        _lastLaneChangeAttempt = 0;
        _laneChangeSourcePointId = -1;
        _laneChangeTargetRegistrationPointId = -1;
        if (_laneChange.IsChangingLane)
        {
            _laneChange.Abort();
        }
        _minObstacleDistance = Random.Shared.Next(8, 13);
        _leaderAcceleration = 0;
        _lastLeaderSpeed = 0;
        _previousAcceleration = 0;
        _perceivedObstacleDistance = float.MaxValue;
        _perceivedObstacleSpeed = 0;
        _obstaclePerceived = true;
        _newObstacleDetectedAt = 0;
        _reactionTimeMs = Random.Shared.NextSingle() *
            (_configuration.ReactionTimeMaxMs - _configuration.ReactionTimeMinMs)
            + _configuration.ReactionTimeMinMs;
        _reactionTimeMs *= _personality.ReactionTimeFactor;
        _gapNoiseState = 0;
        _speedNoiseState = 0;
        _waitingToDriveOff = false;
        _driveOffAt = 0;
        _driveOffElapsed = 0;
        SpawnCounter++;
        Initialized = true;
        Update();
    }

    private void CalculateTangents()
    {
        if (!_junctionEvaluator.TryNext(CurrentSplinePointId, out var nextPointId))
            throw new InvalidOperationException("Cannot get next spline point");

        var points = _spline.Points;
        
        if (_junctionEvaluator.TryPrevious(CurrentSplinePointId, out var previousPointId))
        {
            _startTangent = (points[nextPointId].Position - points[previousPointId].Position) * 0.5f;
        }
        else
        {
            _startTangent = (points[nextPointId].Position - points[CurrentSplinePointId].Position) * 0.5f;
        }

        if (_junctionEvaluator.TryNext(CurrentSplinePointId, out var nextNextPointId, 2))
        {
            _endTangent = (points[nextNextPointId].Position - points[CurrentSplinePointId].Position) * 0.5f;
        }
        else
        {
            _endTangent = (points[nextPointId].Position - points[CurrentSplinePointId].Position) * 0.5f;
        }
    }

    private bool Move(float progress)
    {
        var points = _spline.Points;
        var junctions = _spline.Junctions;
        
        bool recalculateTangents = false;
        while (progress > _currentVecLength)
        {
            progress -= _currentVecLength;
                
            if (!_junctionEvaluator.TryNext(CurrentSplinePointId, out var nextPointId, 1, _nextJunctionId >= 0)
                || !_junctionEvaluator.TryNext(nextPointId, out var nextNextPointId))
            {
                return false;
            }

            CurrentSplinePointId = nextPointId;
            _currentVecLength = (points[nextNextPointId].Position - points[CurrentSplinePointId].Position).Length();
            recalculateTangents = true;

            if (_junctionPassed)
            {
                _endIndicatorDistance -= _currentVecLength;

                if (_endIndicatorDistance < 0)
                {
                    _indicator = 0;
                    _junctionPassed = false;
                    _endIndicatorDistance = 0;
                }
            }
                
            if (_nextJunctionId >= 0 && points[CurrentSplinePointId].JunctionEndId == _nextJunctionId)
            {
                _junctionPassed = true;
                _endIndicatorDistance = junctions[_nextJunctionId].IndicateDistancePost;
                _nextJunctionId = -1;
            }
        }

        if (recalculateTangents)
        {
            CalculateTangents();
        }

        _currentVecProgress = progress;

        return true;
    }

    public bool CanSpawn(int spawnPointId, AiState? previousAi, AiState? nextAi)
    {
        var ops = _spline.Operations;
        ref readonly var spawnPoint = ref ops.Points[spawnPointId];

        if (!IsAllowedLaneCount(spawnPointId))
            return false;
        if (!IsAllowedLane(in spawnPoint))
            return false;
        if (!IsKeepingSafetyDistances(in spawnPoint, previousAi, nextAi))
            return false;

        return EntryCarAi.CanSpawnAiState(spawnPoint.Position, this);
    }

    private bool IsKeepingSafetyDistances(in SplinePoint spawnPoint, AiState? previousAi, AiState? nextAi)
    {
        if (previousAi != null)
        {
            var distance = MathF.Max(0, Vector3.Distance(spawnPoint.Position, previousAi.Status.Position)
                           - previousAi.EntryCarAi.VehicleLengthPreMeters
                           - EntryCarAi.VehicleLengthPostMeters);

            var distanceSquared = distance * distance;
            if (distanceSquared < previousAi.SafetyDistanceSquared || distanceSquared < SafetyDistanceSquared)
                return false;
        }
        
        if (nextAi != null)
        {
            var distance = MathF.Max(0, Vector3.Distance(spawnPoint.Position, nextAi.Status.Position)
                                        - nextAi.EntryCarAi.VehicleLengthPostMeters
                                        - EntryCarAi.VehicleLengthPreMeters);

            var distanceSquared = distance * distance;
            if (distanceSquared < nextAi.SafetyDistanceSquared || distanceSquared < SafetyDistanceSquared)
                return false;
        }

        return true;
    }

    private bool IsAllowedLaneCount(int spawnPointId)
    {
        var laneCount = _spline.GetLanes(spawnPointId).Length;
        if (EntryCarAi.MinLaneCount.HasValue && laneCount < EntryCarAi.MinLaneCount.Value)
            return false;
        if (EntryCarAi.MaxLaneCount.HasValue && laneCount > EntryCarAi.MaxLaneCount.Value)
            return false;
        
        return true;
    }

    private bool IsAllowedLane(in SplinePoint spawnPoint)
    {
        var isAllowedLane = true;
        if (EntryCarAi.AiAllowedLanes != null)
        {
            isAllowedLane = (EntryCarAi.AiAllowedLanes.Contains(LaneSpawnBehavior.Middle) && spawnPoint.LeftId >= 0 && spawnPoint.RightId >= 0)
                            || (EntryCarAi.AiAllowedLanes.Contains(LaneSpawnBehavior.Left) && spawnPoint.LeftId < 0)
                            || (EntryCarAi.AiAllowedLanes.Contains(LaneSpawnBehavior.Right) && spawnPoint.RightId < 0);
        }

        return isAllowedLane;
    }

    private struct SplineLookaheadResult
    {
        public float MaxCorneringSpeed;
        public int LeaderCount;
        public LeaderInfo Leader0, Leader1, Leader2;
        public AiState? ClosestAiState;
        public float ClosestAiStateDistance;
    }

    private SplineLookaheadResult SplineLookahead()
    {
        var points = _spline.Points;
        var junctions = _spline.Junctions;
        int maxLeaders = _configuration.MultiAnticipationCount;

        float maxBrakingDistance = PhysicsUtils.CalculateBrakingDistance(CurrentSpeed, EntryCarAi.AiDeceleration) * 2 + _configuration.LookaheadBufferMeters;
        var result = new SplineLookaheadResult
        {
            MaxCorneringSpeed = float.MaxValue,
            ClosestAiStateDistance = float.MaxValue
        };
        int foundCount = 0;
        AiState? previouslyFoundState = null;
        bool junctionFound = false;
        float distanceTravelled = 0;
        var pointId = CurrentSplinePointId;
        ref readonly var point = ref points[pointId];
        float currentSpeedSquared = CurrentSpeed * CurrentSpeed;
        while (distanceTravelled < maxBrakingDistance)
        {
            distanceTravelled += point.Length;
            pointId = _junctionEvaluator.Next(pointId);
            if (pointId < 0)
                break;

            point = ref points[pointId];

            if (!junctionFound && point.JunctionStartId >= 0 && distanceTravelled < junctions[point.JunctionStartId].IndicateDistancePre)
            {
                ref readonly var jct = ref junctions[point.JunctionStartId];
                bool willTake = _junctionEvaluator.WillTakeJunction(point.JunctionStartId);
                int junctionEndPoint = willTake ? jct.EndPointId : point.NextId;

                // Check if junction endpoint is safe before committing - prevents teleporting into occupied lanes
                if (!IsJunctionEndpointClear(junctionEndPoint))
                {
                    _junctionUnsafe = true;
                    continue;
                }

                var indicator = willTake ? jct.IndicateWhenTaken : jct.IndicateWhenNotTaken;
                if (indicator != 0 && !_junctionUnsafe && CanUseJunction(indicator))
                {
                    _indicator = indicator;
                    _nextJunctionId = point.JunctionStartId;
                    junctionFound = true;
                } else {
                    _junctionUnsafe = true;
                }
            }

            // Collect multiple AI leaders
            if (foundCount < maxLeaders)
            {
                var slowest = _spline.SlowestAiStates[pointId];

                if (slowest != null && slowest != previouslyFoundState)
                {
                    float gap = MathF.Max(0, Vector3.Distance(Status.Position, slowest.Status.Position)
                                              - EntryCarAi.VehicleLengthPreMeters
                                              - slowest.EntryCarAi.VehicleLengthPostMeters);
                    float speed = Math.Min(slowest.CurrentSpeed, slowest.TargetSpeed);
                    // Leader acceleration: only track for leader[0], others get 0
                    float accel = foundCount == 0 ? _leaderAcceleration : 0;

                    var leaderInfo = new LeaderInfo(gap, speed, accel, false);

                    switch (foundCount)
                    {
                        case 0:
                            result.Leader0 = leaderInfo;
                            result.ClosestAiState = slowest;
                            result.ClosestAiStateDistance = gap;
                            break;
                        case 1:
                            result.Leader1 = leaderInfo;
                            break;
                        case 2:
                            result.Leader2 = leaderInfo;
                            break;
                    }

                    foundCount++;
                    previouslyFoundState = slowest;
                }
            }

            float maxCorneringSpeedSquared = PhysicsUtils.CalculateMaxCorneringSpeedSquared(point.Radius, EntryCarAi.AiCorneringSpeedFactor);
            if (maxCorneringSpeedSquared < currentSpeedSquared)
            {
                float maxCorneringSpeed = MathF.Sqrt(maxCorneringSpeedSquared);
                float brakingDistance = PhysicsUtils.CalculateBrakingDistance(CurrentSpeed - maxCorneringSpeed,
                                            EntryCarAi.AiDeceleration * EntryCarAi.AiCorneringBrakeForceFactor)
                                        * EntryCarAi.AiCorneringBrakeDistanceFactor;

                // Comfort factor: start lifting off throttle 50% earlier for smoother cornering
                const float comfortFactor = 1.5f;
                if (brakingDistance * comfortFactor > distanceTravelled)
                {
                    result.MaxCorneringSpeed = Math.Min(maxCorneringSpeed, result.MaxCorneringSpeed);
                }
            }
        }

        result.LeaderCount = foundCount;
        return result;
    }

        
    private bool CanUseJunction(CarStatusFlags indicator)
    {
        var ignorePlayer = ShouldIgnorePlayerObstacles();
        float boxWidth = _configuration.LaneWidthMeters + 1;
        float boxLength = _configuration.EffectiveMaxAiSafetyDistanceMeters + 2;
        float timeHorizon = 3.0f;
        bool isLeft = (indicator & CarStatusFlags.IndicateLeft) != 0;

        var boxCenter = new Vector3(
            Status.Position.X + (isLeft ? -boxWidth : boxWidth),
            Status.Position.Y,
            Status.Position.Z
        );

        float left = boxCenter.X - boxWidth / 2;
        float right = boxCenter.X + boxWidth / 2;
        float front = boxCenter.Z + boxLength;
        float back = boxCenter.Z - boxLength;

        foreach (var car in _entryCarManager.EntryCars) {

            if (!ignorePlayer && car.Client?.HasSentFirstUpdate == true && car.EnableCollisions) {
                Vector3 futurePosition = car.Status.Position + car.Status.Velocity * timeHorizon;
                if (futurePosition.X > left && futurePosition.X < right && futurePosition.Z > back && futurePosition.Z < front) {
                    return false;
                }
            } else if (car.AiControlled) {
                var carAi = _trafficAi.GetAiCarBySessionId(car.SessionId);
                foreach (var aiState in carAi.LastSeenAiState) {
                    if (aiState == null || aiState == this) {
                        continue;
                    }
                    Vector3 futurePosition = aiState.Status.Position + aiState.Status.Velocity * timeHorizon;
                    if (futurePosition.X > left && futurePosition.X < right && futurePosition.Z > back && futurePosition.Z < front) {
                        return false;
                    }
                }
            }
        }
        return true;
    }

    private bool ShouldIgnorePlayerObstacles()
    {
        if (_configuration.IgnorePlayerObstacleSpheres != null)
        {
            foreach (var sphere in _configuration.IgnorePlayerObstacleSpheres)
            {
                if (Vector3.DistanceSquared(Status.Position, sphere.Center) < sphere.RadiusMeters * sphere.RadiusMeters)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private (EntryCar? entryCar, float distance) FindClosestPlayerObstacle()
    {
        if (!ShouldIgnorePlayerObstacles())
        {
            EntryCar? closestCar = null;
            float minDistance = float.MaxValue;
            for (var i = 0; i < _entryCarManager.EntryCars.Length; i++)
            {
                var playerCar = _entryCarManager.EntryCars[i];
                if (playerCar.Client?.HasSentFirstUpdate == true && playerCar.EnableCollisions)
                {
                    float distance = Vector3.DistanceSquared(playerCar.Status.Position, Status.Position);

                    float minAngle = 180 - _configuration.PlayerDetectionAngleRange;
                    float maxAngle = 180 + _configuration.PlayerDetectionAngleRange;
                    float angle = GetAngleToCar(playerCar.Status);
                    if (distance < minDistance
                        && Math.Abs(playerCar.Status.Position.Y - Status.Position.Y) < _configuration.PlayerDetectionHeightThreshold
                        && angle > minAngle && angle < maxAngle)
                    {
                        minDistance = distance;
                        closestCar = playerCar;
                    }
                }
            }

            if (closestCar != null)
            {
                return (closestCar, MathF.Sqrt(minDistance));
            }
        }

        return (null, float.MaxValue);
    }

    private bool IsObstacle(EntryCar playerCar)
    {
        float aiRectWidth = 4; // Lane width
        float halfAiRectWidth = aiRectWidth / 2;
        float aiRectLength = 10; // length of rectangle infront of ai traffic
        float aiRectOffset = 1; // offset of the rectangle from ai position

        float obstacleRectWidth = 1; // width of obstacle car 
        float obstacleRectLength = 1; // length of obstacle car
        float halfObstacleRectWidth = obstacleRectWidth / 2;
        float halfObstanceRectLength = obstacleRectLength / 2;

        Vector3 forward = Vector3.Transform(-Vector3.UnitX, Matrix4x4.CreateRotationY(Status.Rotation.X));
        Matrix4x4 aiViewMatrix = Matrix4x4.CreateLookAt(Status.Position, Status.Position + forward, Vector3.UnitY);

        Matrix4x4 targetWorldViewMatrix = Matrix4x4.CreateRotationY(playerCar.Status.Rotation.X) * Matrix4x4.CreateTranslation(playerCar.Status.Position) * aiViewMatrix;

        Vector3 targetFrontLeft = Vector3.Transform(new Vector3(-halfObstanceRectLength, 0, halfObstacleRectWidth), targetWorldViewMatrix);
        Vector3 targetFrontRight = Vector3.Transform(new Vector3(-halfObstanceRectLength, 0, -halfObstacleRectWidth), targetWorldViewMatrix);
        Vector3 targetRearLeft = Vector3.Transform(new Vector3(halfObstanceRectLength, 0, halfObstacleRectWidth), targetWorldViewMatrix);
        Vector3 targetRearRight = Vector3.Transform(new Vector3(halfObstanceRectLength, 0, -halfObstacleRectWidth), targetWorldViewMatrix);

        static bool IsPointInside(Vector3 point, float width, float length, float offset)
            => MathF.Abs(point.X) >= width || (-point.Z >= offset && -point.Z <= offset + length);

        bool isObstacle = IsPointInside(targetFrontLeft, halfAiRectWidth, aiRectLength, aiRectOffset)
                          || IsPointInside(targetFrontRight, halfAiRectWidth, aiRectLength, aiRectOffset)
                          || IsPointInside(targetRearLeft, halfAiRectWidth, aiRectLength, aiRectOffset)
                          || IsPointInside(targetRearRight, halfAiRectWidth, aiRectLength, aiRectOffset);

        return isObstacle;
    }

    public void DetectObstacles()
    {
        if (!Initialized) return;
        DetectObstaclesIdm();
    }

    private void DetectObstaclesIdm()
    {
        if (_sessionManager.ServerTimeMilliseconds < _ignoreObstaclesUntil)
        {
            // Still check for emergency collision avoidance with AI cars
            var emergencyLookahead = SplineLookahead();
            var emergencyPlayerObstacle = FindClosestPlayerObstacle();
            float closestObstacle = Math.Min(emergencyLookahead.ClosestAiStateDistance, emergencyPlayerObstacle.distance);

            if (closestObstacle < _minObstacleDistance * 2)
            {
                // Emergency brake even when ignoring obstacles
                Acceleration = -EntryCarAi.AiDeceleration;
                TargetSpeed = 0;
                return;
            }

            // Otherwise continue accelerating
            Acceleration = EntryCarAi.AiAcceleration * _personality.AccelerationFactor;
            return;
        }

        if (_sessionManager.ServerTimeMilliseconds < _stoppedForCollisionUntil)
        {
            Acceleration = -EntryCarAi.AiDeceleration * _personality.DecelerationFactor;
            TargetSpeed = 0;
            return;
        }

        var splineLookahead = SplineLookahead();
        var playerObstacle = FindClosestPlayerObstacle();

        ClosestAiObstacleDistance = splineLookahead.ClosestAiState != null ? splineLookahead.ClosestAiStateDistance : -1;

        // Calculate desired speed considering road/cornering limits and personality
        _desiredSpeed = IdmBrain.CalculateDesiredSpeed(
            InitialMaxSpeed,
            splineLookahead.MaxCorneringSpeed,
            in _personality);

        // Build leader array for multi-anticipation
        // If player is closest obstacle, use single-leader mode (can't see through players)
        bool playerIsClosest = playerObstacle.distance < splineLookahead.ClosestAiStateDistance
                               && playerObstacle.entryCar != null;

        float obstacleDistance; // Leader[0] distance for perception delay, progressive braking, lane change
        float obstacleSpeed;   // Leader[0] speed

        Span<LeaderInfo> leaders = stackalloc LeaderInfo[3];
        int leaderCount;

        if (playerIsClosest)
        {
            float pSpeed = playerObstacle.entryCar!.Status.Velocity.Length();
            if (pSpeed < 0.1f) pSpeed = 0;
            leaders[0] = new LeaderInfo(playerObstacle.distance, pSpeed, _leaderAcceleration, true);
            leaderCount = 1;
            obstacleDistance = playerObstacle.distance;
            obstacleSpeed = pSpeed;
        }
        else if (splineLookahead.LeaderCount > 0)
        {
            // Use multi-anticipation with AI leaders
            leaderCount = splineLookahead.LeaderCount;
            leaders[0] = splineLookahead.Leader0;
            if (leaderCount > 1) leaders[1] = splineLookahead.Leader1;
            if (leaderCount > 2) leaders[2] = splineLookahead.Leader2;
            obstacleDistance = splineLookahead.Leader0.Gap;
            obstacleSpeed = splineLookahead.Leader0.Speed;
        }
        else
        {
            leaderCount = 0;
            obstacleDistance = float.MaxValue;
            obstacleSpeed = 0;
        }

        // Perception delay for cut-ins (Change 4) — applies to leader[0] only
        float emergencyZoneStart = _minObstacleDistance * 3;
        long currentTime = _sessionManager.ServerTimeMilliseconds;
        if (obstacleDistance < float.MaxValue)
        {
            // Detect new obstacle (gap halved suddenly and reasonably close)
            bool isNewObstacle = obstacleDistance < _perceivedObstacleDistance * 0.5f
                                 && obstacleDistance < emergencyZoneStart * 2;

            if (isNewObstacle && _obstaclePerceived)
            {
                _obstaclePerceived = false;
                _newObstacleDetectedAt = currentTime;
            }

            if (!_obstaclePerceived)
            {
                if (currentTime - _newObstacleDetectedAt > _reactionTimeMs)
                {
                    _obstaclePerceived = true;
                }
                else
                {
                    if (obstacleDistance > _minObstacleDistance * 1.5f)
                    {
                        obstacleDistance = _perceivedObstacleDistance;
                        obstacleSpeed = _perceivedObstacleSpeed;
                    }
                    else
                    {
                        _obstaclePerceived = true;
                    }
                }
            }

            if (_obstaclePerceived)
            {
                _perceivedObstacleDistance = obstacleDistance;
                _perceivedObstacleSpeed = obstacleSpeed;
            }

            // Apply estimation noise (Change 5E) - Wiener process for leader[0]
            float dt = 0.1f;
            float correlationDecay = MathF.Exp(-dt / 10.0f);
            float noiseScale = MathF.Sqrt(1 - correlationDecay * correlationDecay);
            _gapNoiseState = correlationDecay * _gapNoiseState + noiseScale * RandomNormal();
            _speedNoiseState = correlationDecay * _speedNoiseState + noiseScale * RandomNormal();

            float gapNoise = 1.0f + _gapNoiseState * _configuration.GapEstimationError;
            float speedNoise = 1.0f + _speedNoiseState * _configuration.SpeedEstimationError;
            obstacleDistance = MathF.Max(obstacleDistance * gapNoise, 0.1f);
            obstacleSpeed = MathF.Max(obstacleSpeed * speedNoise, 0);

            // Update leader[0] with noisy values
            leaders[0] = new LeaderInfo(obstacleDistance, obstacleSpeed, leaders[0].Acceleration, leaders[0].IsPlayer);

            // Apply independent noise to leaders 1-2
            for (int i = 1; i < leaderCount; i++)
            {
                float iGapNoise = 1.0f + RandomNormal() * _configuration.GapEstimationError;
                float iSpeedNoise = 1.0f + RandomNormal() * _configuration.SpeedEstimationError;
                leaders[i] = new LeaderInfo(
                    MathF.Max(leaders[i].Gap * iGapNoise, 0.1f),
                    MathF.Max(leaders[i].Speed * iSpeedNoise, 0),
                    leaders[i].Acceleration,
                    leaders[i].IsPlayer);
            }
        }
        else
        {
            _perceivedObstacleDistance = float.MaxValue;
            _perceivedObstacleSpeed = 0;
            _obstaclePerceived = true;
        }

        // Only use IDM car-following if there's actual collision risk (based on leader[0])
        bool shouldFollowObstacle = obstacleDistance < float.MaxValue &&
            (obstacleDistance < _configuration.IdmBaseTimeHeadwaySeconds * CurrentSpeed * 3 ||
             CurrentSpeed >= obstacleSpeed - 2.0f ||
             obstacleDistance < _minObstacleDistance * 3);

        // Calculate IDM acceleration using multi-anticipation
        float idmAcceleration;
        if (!shouldFollowObstacle)
        {
            // No relevant obstacle - free-flow
            idmAcceleration = IdmBrain.CalculateMultiAnticipativeAcceleration(
                CurrentSpeed, _desiredSpeed,
                leaders, 0,
                in _personality, _configuration,
                EntryCarAi.AiAcceleration, EntryCarAi.AiDeceleration);
        }
        else
        {
            idmAcceleration = IdmBrain.CalculateMultiAnticipativeAcceleration(
                CurrentSpeed, _desiredSpeed,
                leaders, leaderCount,
                in _personality, _configuration,
                EntryCarAi.AiAcceleration, EntryCarAi.AiDeceleration);
        }

        // Drive-off delay: add realistic delay before accelerating from a stop
        if (CurrentSpeed < 0.1f && _stoppedForObstacle && idmAcceleration > 0 && !_waitingToDriveOff)
        {
            // Obstacle just cleared while stopped — start delay timer
            float delay = Random.Shared.NextSingle()
                * (_configuration.DriveOffDelayMaxSeconds - _configuration.DriveOffDelayMinSeconds)
                + _configuration.DriveOffDelayMinSeconds;
            delay *= _personality.DriveOffDelayFactor;
            _waitingToDriveOff = true;
            _driveOffAt = currentTime + (long)(delay * 1000);
            _driveOffRampTime = _configuration.DriveOffRampSeconds * _personality.DriveOffDelayFactor;
            _driveOffElapsed = 0;
        }

        if (_waitingToDriveOff)
        {
            // Cancel if obstacle reappears (hard braking requested)
            if (idmAcceleration < -0.5f)
            {
                _waitingToDriveOff = false;
            }
            else if (currentTime < _driveOffAt)
            {
                // Still in delay — keep stopped
                idmAcceleration = 0;
            }
            else
            {
                // Ramp up with quadratic ease-in
                _driveOffElapsed += 0.1f; // 100ms update interval
                float rampFraction = Math.Clamp(_driveOffElapsed / _driveOffRampTime, 0, 1);
                idmAcceleration *= rampFraction * rampFraction;
                if (rampFraction >= 1.0f)
                {
                    _waitingToDriveOff = false;
                }
            }
        }

        // Progressive braking zone: blends IDM with emergency braking based on proximity
        if (obstacleDistance < emergencyZoneStart)
        {
            // t = 1.0 at _minObstacleDistance, 0.0 at emergencyZoneStart
            float t = 1.0f - Math.Clamp(
                (obstacleDistance - _minObstacleDistance) / (emergencyZoneStart - _minObstacleDistance),
                0f, 1f);

            // Quadratic ease-in: gentle at first, hard near contact
            float brakeFactor = t * t;

            float emergencyBraking = -EntryCarAi.AiDeceleration * 2.0f;
            Acceleration = idmAcceleration + (emergencyBraking - idmAcceleration) * brakeFactor;
            TargetSpeed = obstacleDistance < _minObstacleDistance
                ? 0
                : Math.Max(0, obstacleSpeed * (1.0f - brakeFactor));
            MaxSpeed = InitialMaxSpeed;

            if (obstacleDistance < _minObstacleDistance)
            {
                HandleStoppedForObstacle();
                return;
            }
        }
        else
        {
            Acceleration = idmAcceleration;
            TargetSpeed = idmAcceleration >= 0 ? _desiredSpeed : Math.Max(0, obstacleSpeed);
            MaxSpeed = InitialMaxSpeed;
        }

        // Track leader acceleration for CAH model (leader[0] speed only)
        _leaderAcceleration = UpdateLeaderAcceleration(obstacleSpeed);

        // Consider lane change using overtake desire (leader[0] data only)
        if (!_laneChange.IsChangingLane)
        {
            ConsiderLaneChange(obstacleDistance, obstacleSpeed);
        }

        HandleStoppedForObstacle();
    }

    /// <summary>
    /// Box-Muller transform for generating normally distributed random numbers.
    /// </summary>
    private static float RandomNormal()
    {
        float u1 = 1.0f - (float)Random.Shared.NextDouble(); // Uniform(0,1] to avoid log(0)
        float u2 = (float)Random.Shared.NextDouble();
        return MathF.Sqrt(-2.0f * MathF.Log(u1)) * MathF.Cos(2.0f * MathF.PI * u2);
    }

    private float UpdateLeaderAcceleration(float currentLeaderSpeed)
    {
        float dt = 0.1f; // obstacle detection runs at 100ms intervals
        float accel = (currentLeaderSpeed - _lastLeaderSpeed) / dt;
        _lastLeaderSpeed = currentLeaderSpeed;
        return accel;
    }

    private void ConsiderLaneChange(float obstacleDistance, float obstacleSpeed)
    {
        if (!_configuration.EnableLaneChanging) return;
        if (_nextJunctionId >= 0) return;

        long currentTime = _sessionManager.ServerTimeMilliseconds;
        if (currentTime - _lastLaneChangeAttempt < _configuration.LaneChangeCooldownMilliseconds)
            return;

        var points = _spline.Points;
        ref readonly var currentPoint = ref points[CurrentSplinePointId];

        // Calculate overtake desire
        _lastOvertakeDesire = IdmBrain.CalculateOvertakeDesire(
            CurrentSpeed,
            _desiredSpeed,
            obstacleDistance,
            obstacleSpeed,
            in _personality,
            _configuration);

        // Threshold for overtake scaled by aggressiveness
        float threshold = _configuration.OvertakeDesireThreshold / _personality.Aggressiveness;

        // Overtake if desire exceeds threshold - try left first, then right
        if (_lastOvertakeDesire > threshold)
        {
            _lastLaneChangeAttempt = currentTime;

            // Prefer left (fast lane)
            if (currentPoint.LeftId >= 0 && TryInitiateLaneChange(currentPoint.LeftId, true))
                return;
            // Fall back to right if left unavailable or blocked
            if (currentPoint.RightId >= 0 && TryInitiateLaneChange(currentPoint.RightId, false))
                return;
        }
    }

    private void HandleStoppedForObstacle()
    {
        // Adjust honk timing based on patience
        int baseHonkDelay = (int)(3000 * _personality.Patience);
        int honkDelayVariation = (int)(4000 * _personality.Patience);
        int ignoreTimeout = (int)(_configuration.IgnoreObstaclesAfterMilliseconds * _personality.Patience);

        if (CurrentSpeed == 0 && !_stoppedForObstacle)
        {
            _stoppedForObstacle = true;
            _stoppedForObstacleSince = _sessionManager.ServerTimeMilliseconds;
            _obstacleHonkStart = _stoppedForObstacleSince + Random.Shared.Next(baseHonkDelay, baseHonkDelay + honkDelayVariation);
            _obstacleHonkEnd = _obstacleHonkStart + Random.Shared.Next(500, 1500);
            Log.Verbose("AI {SessionId} stopped for obstacle", EntryCarAi.EntryCar.SessionId);
        }
        else if (CurrentSpeed > 0 && _stoppedForObstacle)
        {
            _stoppedForObstacle = false;
            Log.Verbose("AI {SessionId} no longer stopped for obstacle", EntryCarAi.EntryCar.SessionId);
        }
        else if (_stoppedForObstacle && _sessionManager.ServerTimeMilliseconds - _stoppedForObstacleSince > ignoreTimeout)
        {
            _ignoreObstaclesUntil = _sessionManager.ServerTimeMilliseconds + _configuration.IgnoreModeDurationMilliseconds;
            Log.Verbose("AI {SessionId} ignoring obstacles until {IgnoreObstaclesUntil}", EntryCarAi.EntryCar.SessionId, _ignoreObstaclesUntil);
        }
    }

    public void StopForCollision()
    {
        if (!ShouldIgnorePlayerObstacles())
        {
            _stoppedForCollisionUntil = _sessionManager.ServerTimeMilliseconds + Random.Shared.Next(EntryCarAi.AiMinCollisionStopTimeMilliseconds, EntryCarAi.AiMaxCollisionStopTimeMilliseconds);
        }

        // Abort any in-progress lane change on collision
        if (_laneChange.IsChangingLane)
        {
            AbortLaneChange();
        }
    }

    private bool IsTargetLaneClear(int targetPointId, bool isOvertake)
    {
        if (targetPointId < 0) return false;

        var points = _spline.Points;
        ref readonly var targetPoint = ref points[targetPointId];

        // Check that target lane goes in same direction
        if (!_spline.Operations.IsSameDirection(CurrentSplinePointId, targetPointId))
            return false;

        float lookAheadDistance = _configuration.LaneChangeMaxDistanceMeters * _configuration.LaneChangeLookAheadMultiplier;
        float lookBehindDistance = _configuration.LaneChangeMaxDistanceMeters * _configuration.LaneChangeLookBehindMultiplier;
        float timeHorizon = _configuration.LaneChangeTimeHorizonSeconds;

        // Minimum safe distance - never merge if someone is this close
        float minSafeDistance = _configuration.LaneChangeMinDistanceMeters * 0.5f;
        // Extended safe distance for cars that are also lane changing
        float laneChangeSafeDistance = _configuration.LaneChangeMaxDistanceMeters;

        // Look ahead in target lane - check for AI blocking
        float distanceAhead = 0;
        int checkPointId = targetPointId;
        while (distanceAhead < lookAheadDistance && checkPointId >= 0)
        {
            var slowest = _spline.SlowestAiStates[checkPointId];
            if (slowest != null && slowest != this)
            {
                // Calculate actual physical distance
                float physicalDistance = Vector3.Distance(Status.Position, slowest.Status.Position);

                // Always reject if too close, regardless of speed
                if (physicalDistance < minSafeDistance)
                {
                    return false;
                }

                // If they're also lane changing, check for collision course
                if (slowest.IsCurrentlyLaneChanging)
                {
                    // If they're lane changing toward our current lane (opposite direction), definite collision
                    if (slowest.IsLaneChangeOvertake != isOvertake && physicalDistance < laneChangeSafeDistance * 1.5f)
                    {
                        return false;
                    }
                    // Any lane change nearby is dangerous
                    if (physicalDistance < laneChangeSafeDistance)
                    {
                        return false;
                    }
                }

                // Someone is ahead in target lane - check speed
                float theirSpeed = Math.Min(slowest.CurrentSpeed, slowest.TargetSpeed);
                if (theirSpeed < CurrentSpeed - _configuration.LaneChangeSpeedThresholdMs * 0.5f)
                {
                    // They're slower than us, lane not clear for overtake
                    return false;
                }
            }

            distanceAhead += points[checkPointId].Length;
            checkPointId = points[checkPointId].NextId;
        }

        // Look behind in target lane - check for faster AI approaching
        float distanceBehind = 0;
        checkPointId = targetPointId;
        while (distanceBehind < lookBehindDistance && checkPointId >= 0)
        {
            var slowest = _spline.SlowestAiStates[checkPointId];
            if (slowest != null && slowest != this)
            {
                // Calculate actual physical distance
                float physicalDistance = Vector3.Distance(Status.Position, slowest.Status.Position);

                // Always reject if too close, regardless of speed
                if (physicalDistance < minSafeDistance)
                {
                    return false;
                }

                // If they're also lane changing, check for collision course
                if (slowest.IsCurrentlyLaneChanging)
                {
                    // If they're lane changing toward our current lane (opposite direction), definite collision
                    if (slowest.IsLaneChangeOvertake != isOvertake && physicalDistance < laneChangeSafeDistance * 1.5f)
                    {
                        return false;
                    }
                    // Any lane change nearby is dangerous
                    if (physicalDistance < laneChangeSafeDistance)
                    {
                        return false;
                    }
                }

                // Someone is behind in target lane - check speed
                float theirSpeed = slowest.CurrentSpeed;
                if (theirSpeed > CurrentSpeed + _configuration.LaneChangeSpeedThresholdMs)
                {
                    // They're faster and approaching, lane not clear
                    return false;
                }
            }

            distanceBehind += points[checkPointId].Length;
            checkPointId = points[checkPointId].PreviousId;
        }

        // Check our current lane for cars also lane changing to the same target (parallel merge collision)
        float distanceInCurrentLane = 0;
        checkPointId = CurrentSplinePointId;
        while (distanceInCurrentLane < lookAheadDistance && checkPointId >= 0)
        {
            var slowest = _spline.SlowestAiStates[checkPointId];
            if (slowest != null && slowest != this && slowest.IsCurrentlyLaneChanging)
            {
                // Check if they're lane changing in the same direction (both left or both right)
                // isOvertake = true means going left, false means going right
                if (slowest.IsLaneChangeOvertake == isOvertake)
                {
                    // They're targeting the same lane, too dangerous if close
                    float physicalDistance = Vector3.Distance(Status.Position, slowest.Status.Position);
                    if (physicalDistance < laneChangeSafeDistance)
                    {
                        return false;
                    }
                }
            }
            distanceInCurrentLane += points[checkPointId].Length;
            checkPointId = points[checkPointId].NextId;
        }

        // Check for players in target lane area
        if (!ShouldIgnorePlayerObstacles())
        {
            var targetPosition = targetPoint.Position;
            foreach (var car in _entryCarManager.EntryCars)
            {
                if (car.Client?.HasSentFirstUpdate == true && car.EnableCollisions)
                {
                    Vector3 futurePosition = car.Status.Position + car.Status.Velocity * timeHorizon;
                    float distanceSquared = Vector3.DistanceSquared(futurePosition, targetPosition);
                    if (distanceSquared < _configuration.LaneWidthMeters * _configuration.LaneWidthMeters * 4)
                    {
                        return false;
                    }

                    // Check behind for approaching players
                    if (_configuration.LaneChangeCheckBehindForPlayers)
                    {
                        float playerDistance = Vector3.Distance(car.Status.Position, Status.Position);
                        if (playerDistance < _configuration.LaneChangePlayerLookBehindMeters)
                        {
                            // Check if player is behind us and approaching
                            float angleToPlayer = GetAngleToCar(car.Status);
                            bool playerIsBehind = angleToPlayer < 90 || angleToPlayer > 270;

                            if (playerIsBehind)
                            {
                                float playerSpeed = car.Status.Velocity.Length();
                                // If player is faster and close behind, don't merge
                                if (playerSpeed > CurrentSpeed + _configuration.LaneChangeSpeedThresholdMs)
                                {
                                    return false;
                                }
                            }
                        }
                    }
                }
            }
        }

        // Additional physical proximity check - catches cars not tracked by SlowestAiStates
        if (IsAnyAiInTargetLaneCorridor(targetPointId, isOvertake, lookAheadDistance, lookBehindDistance, minSafeDistance, laneChangeSafeDistance))
            return false;

        return true;
    }

    /// <summary>
    /// Physical proximity check that iterates ALL AI states to find cars that might be in the target lane corridor.
    /// This is a belt-and-suspenders approach to catch cars that SlowestAiStates misses (faster cars at same spline point).
    /// </summary>
    private bool IsAnyAiInTargetLaneCorridor(int targetPointId, bool isOvertake,
        float lookAheadDistance, float lookBehindDistance, float minSafeDistance, float laneChangeSafeDistance)
    {
        var targetPosition = _spline.Points[targetPointId].Position;
        float relevantRadiusSquared = (lookAheadDistance + lookBehindDistance + 50) * (lookAheadDistance + lookBehindDistance + 50);

        foreach (var car in _entryCarManager.EntryCars)
        {
            if (!car.AiControlled) continue;

            var carAi = _trafficAi.GetAiCarBySessionId(car.SessionId);
            foreach (var aiState in carAi.LastSeenAiState)
            {
                // Cast to AiState to check Initialized status
                if (aiState == null || aiState == this) continue;
                if (aiState is AiState typedState && !typedState.Initialized) continue;

                // Early distance cull
                float distanceSquared = Vector3.DistanceSquared(Status.Position, aiState.Status.Position);
                if (distanceSquared > relevantRadiusSquared) continue;

                // Check if this car is in the target lane (physically close to target corridor)
                if (!IsCarInTargetLane(aiState, targetPointId)) continue;

                float physicalDistance = MathF.Sqrt(distanceSquared);

                // Too close - reject
                if (physicalDistance < minSafeDistance) return true;

                // Check lane changing cars (if it's an AiState, check lane change status)
                if (aiState is AiState otherAiState && otherAiState.IsCurrentlyLaneChanging && physicalDistance < laneChangeSafeDistance * 1.5f)
                    return true;

                // Check relative speeds (car ahead is slower, or car behind is faster)
                bool isAhead = IsCarAhead(aiState);
                float theirSpeed = aiState.Status.Velocity.Length();
                if (isAhead && theirSpeed < CurrentSpeed - _configuration.LaneChangeSpeedThresholdMs * 0.5f)
                    return true;
                if (!isAhead && theirSpeed > CurrentSpeed + _configuration.LaneChangeSpeedThresholdMs)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Check if another car is in the target lane by examining if their position is close to the target lane corridor.
    /// </summary>
    private bool IsCarInTargetLane(IAiState other, int targetPointId)
    {
        var points = _spline.Points;

        // Check if the car is physically close to the target lane spline points
        // Walk forward and backward from target point to see if car is near any of them
        int checkId = targetPointId;
        for (int i = 0; i < 10 && checkId >= 0; i++) // Check several points ahead
        {
            float distToPoint = Vector3.Distance(other.Status.Position, points[checkId].Position);
            if (distToPoint < _configuration.LaneWidthMeters * 1.5f)
                return true;
            checkId = points[checkId].NextId;
        }

        checkId = targetPointId;
        for (int i = 0; i < 10 && checkId >= 0; i++) // Check several points behind
        {
            float distToPoint = Vector3.Distance(other.Status.Position, points[checkId].Position);
            if (distToPoint < _configuration.LaneWidthMeters * 1.5f)
                return true;
            checkId = points[checkId].PreviousId;
        }

        return false;
    }

    /// <summary>
    /// Check if another car is ahead of us based on velocity direction.
    /// </summary>
    private bool IsCarAhead(IAiState other)
    {
        Vector3 toOther = other.Status.Position - Status.Position;
        Vector3 ourDirection = Status.Velocity.LengthSquared() > 0.01f
            ? Vector3.Normalize(Status.Velocity)
            : Vector3.Normalize(_spline.GetForwardVector(CurrentSplinePointId));
        return Vector3.Dot(toOther, ourDirection) > 0;
    }

    /// <summary>
    /// Check if a junction endpoint is clear of other AI traffic.
    /// This prevents cars from teleporting into occupied lanes at junction points.
    /// </summary>
    private bool IsJunctionEndpointClear(int endPointId)
    {
        if (endPointId < 0) return true;

        float safeDistanceSquared = _configuration.LaneChangeMinDistanceMeters * _configuration.LaneChangeMinDistanceMeters;
        var endPosition = _spline.Points[endPointId].Position;

        foreach (var car in _entryCarManager.EntryCars)
        {
            if (!car.AiControlled) continue;

            var carAi = _trafficAi.GetAiCarBySessionId(car.SessionId);
            foreach (var aiState in carAi.LastSeenAiState)
            {
                // Cast to AiState to check Initialized status
                if (aiState == null || aiState == this) continue;
                if (aiState is AiState typedState && !typedState.Initialized) continue;

                if (Vector3.DistanceSquared(aiState.Status.Position, endPosition) < safeDistanceSquared)
                    return false;
            }
        }
        return true;
    }

    private bool ValidateLaneChangeCurve(
        Vector3 startPosition, Vector3 endPosition,
        Vector3 startTangent, Vector3 endTangent,
        int sourcePointId, int targetPointId, float laneChangeDistance)
    {
        var points = _spline.Points;
        const float maxDeviationMeters = 8f;

        for (int i = 1; i <= 3; i++)
        {
            float t = i * 0.25f;
            Vector3 curvePos = CatmullRom.CalculatePosition(startPosition, endPosition, startTangent, endTangent, t);

            // Find closest distance to source lane spline points
            float minDistance = float.MaxValue;
            float distanceTravelled = 0;
            int checkId = sourcePointId;
            while (distanceTravelled < laneChangeDistance && checkId >= 0)
            {
                float dist = Vector3.Distance(curvePos, points[checkId].Position);
                if (dist < minDistance) minDistance = dist;
                distanceTravelled += points[checkId].Length;
                checkId = points[checkId].NextId;
            }

            // Find closest distance to target lane spline points
            distanceTravelled = 0;
            checkId = targetPointId;
            while (distanceTravelled < laneChangeDistance && checkId >= 0)
            {
                float dist = Vector3.Distance(curvePos, points[checkId].Position);
                if (dist < minDistance) minDistance = dist;
                distanceTravelled += points[checkId].Length;
                checkId = points[checkId].NextId;
            }

            if (minDistance > maxDeviationMeters)
                return false;
        }

        return true;
    }

    private bool IsLaneChangePathStraightEnough(int sourcePointId, int targetPointId, float distance)
    {
        var points = _spline.Points;
        const float minRadius = 200f;

        // Check source lane points
        float distanceTravelled = 0;
        int checkId = sourcePointId;
        while (distanceTravelled < distance && checkId >= 0)
        {
            if (points[checkId].Radius > 0 && points[checkId].Radius < minRadius)
                return false;
            distanceTravelled += points[checkId].Length;
            checkId = points[checkId].NextId;
        }

        // Check target lane points
        distanceTravelled = 0;
        checkId = targetPointId;
        while (distanceTravelled < distance && checkId >= 0)
        {
            if (points[checkId].Radius > 0 && points[checkId].Radius < minRadius)
                return false;
            distanceTravelled += points[checkId].Length;
            checkId = points[checkId].NextId;
        }

        return true;
    }

    private bool TryInitiateLaneChange(int targetPointId, bool isOvertake)
    {
        if (!IsTargetLaneClear(targetPointId, isOvertake))
            return false;

        var points = _spline.Points;
        ref readonly var currentPoint = ref points[CurrentSplinePointId];
        ref readonly var targetPoint = ref points[targetPointId];

        // Calculate lane change distance based on current speed
        float speedFactor = Math.Clamp(CurrentSpeed / _configuration.MaxSpeedMs, _configuration.LaneChangeSpeedFactorMin, _configuration.LaneChangeSpeedFactorMax);
        float laneChangeDistance = _configuration.LaneChangeMinDistanceMeters +
            ((_configuration.LaneChangeMaxDistanceMeters - _configuration.LaneChangeMinDistanceMeters) * speedFactor);

        // Reject lane change if path goes through corners
        if (!IsLaneChangePathStraightEnough(CurrentSplinePointId, targetPointId, laneChangeDistance))
            return false;

        // Find target point ahead in target lane
        float distanceAhead = 0;
        int endPointId = targetPointId;
        while (distanceAhead < laneChangeDistance && endPointId >= 0)
        {
            distanceAhead += points[endPointId].Length;
            int nextId = points[endPointId].NextId;
            if (nextId < 0) break;
            endPointId = nextId;
        }

        if (endPointId < 0 || distanceAhead < _configuration.LaneChangeMinDistanceMeters)
            return false;

        ref readonly var endPoint = ref points[endPointId];

        // Compute Catmull-Rom curve parameters
        Vector3 startPosition = Status.Position;
        startPosition = startPosition with { Y = startPosition.Y - EntryCarAi.AiSplineHeightOffsetMeters };
        Vector3 endPosition = endPoint.Position;

        // Tangents: current direction and target lane direction, scaled by distance
        Vector3 startTangent = Vector3.Normalize(Status.Velocity.Length() > 0.1f ? Status.Velocity : _spline.GetForwardVector(CurrentSplinePointId)) * laneChangeDistance * 0.5f;
        Vector3 endTangent = Vector3.Normalize(_spline.GetForwardVector(endPointId)) * laneChangeDistance * 0.5f;

        // Validate the curve doesn't deviate too far from the road
        if (!ValidateLaneChangeCurve(startPosition, endPosition, startTangent, endTangent,
                CurrentSplinePointId, targetPointId, laneChangeDistance))
            return false;

        // Get camber values for interpolation (use interpolated camber at car's actual position)
        float startCamber = _spline.Operations.GetCamber(CurrentSplinePointId,
            _currentVecLength > 0 ? _currentVecProgress / _currentVecLength : 0);
        float endCamber = _spline.Operations.GetCamber(endPointId);

        // Set indicator
        _indicator = isOvertake ? CarStatusFlags.IndicateLeft : CarStatusFlags.IndicateRight;

        // Register in target lane for collision avoidance (source lane already tracked via CurrentSplinePointId)
        _laneChangeSourcePointId = CurrentSplinePointId;
        _laneChangeTargetRegistrationPointId = targetPointId; // Track the adjacent point we're registering at
        _spline.SlowestAiStates.Enter(targetPointId, this);

        // Begin lane change
        _laneChange.Begin(
            CurrentSplinePointId,
            endPointId,
            targetPointId,
            startPosition,
            endPosition,
            startTangent,
            endTangent,
            startCamber,
            endCamber,
            isOvertake,
            points);

        Log.Verbose("AI {SessionId} starting lane change from point {SourcePoint} to {TargetPoint}, overtake={IsOvertake}",
            EntryCarAi.EntryCar.SessionId, CurrentSplinePointId, endPointId, isOvertake);

        return true;
    }

    private void CompleteLaneChange()
    {
        if (!_laneChange.IsChangingLane) return;

        int targetPointId = _laneChange.TargetPointId;

        // Leave the old source lane registration
        if (_laneChangeSourcePointId >= 0)
        {
            _spline.SlowestAiStates.Leave(_laneChangeSourcePointId, this);
            _laneChangeSourcePointId = -1;
        }

        // Leave the target lane registration from initiation (the adjacent point, not the end point)
        if (_laneChangeTargetRegistrationPointId >= 0)
        {
            _spline.SlowestAiStates.Leave(_laneChangeTargetRegistrationPointId, this);
            _laneChangeTargetRegistrationPointId = -1;
        }

        // Update current point to target lane
        // Note: We need to manually set _currentSplinePointId to avoid the property's Enter/Leave logic
        // since we already registered in the target lane when initiating
        _spline.SlowestAiStates.Leave(_currentSplinePointId, this);
        _currentSplinePointId = targetPointId;
        _spline.SlowestAiStates.Enter(targetPointId, this);

        // Reset junction evaluator for new lane
        _junctionEvaluator.Clear();

        // Recalculate tangents for new lane
        if (!_junctionEvaluator.TryNext(CurrentSplinePointId, out var nextPointId))
        {
            Log.Debug("Car {SessionId} cannot find next point after lane change, despawning", EntryCarAi.EntryCar.SessionId);
            _laneChange.Complete();
            Despawn();
            return;
        }

        _currentVecLength = (_spline.Points[nextPointId].Position - _spline.Points[CurrentSplinePointId].Position).Length();
        _currentVecProgress = 0;
        CalculateTangents();

        // Clear indicator after a short distance
        _endIndicatorDistance = _configuration.LaneChangeIndicatorClearDistanceMeters;
        _junctionPassed = true; // Reuse the junction indicator logic for clearing

        // Adjust max speed for new lane (right lanes get speed offset)
        // Calculate the old lane offset and new lane offset
        bool oldLaneHadLeft = _laneChange.SourcePointId >= 0 && _spline.Points[_laneChange.SourcePointId].LeftId >= 0;
        bool newLaneHasLeft = _spline.Points[CurrentSplinePointId].LeftId >= 0;
        float oldLaneOffset = oldLaneHadLeft ? _configuration.RightLaneOffsetMs : 0;
        float newLaneOffset = newLaneHasLeft ? _configuration.RightLaneOffsetMs : 0;
        float speedAdjustment = newLaneOffset - oldLaneOffset;
        InitialMaxSpeed += speedAdjustment;
        MaxSpeed = Math.Max(MaxSpeed, InitialMaxSpeed);

        _laneChange.Complete();

        Log.Verbose("AI {SessionId} completed lane change to point {TargetPoint}", EntryCarAi.EntryCar.SessionId, targetPointId);
    }

    private void AbortLaneChange()
    {
        if (!_laneChange.IsChangingLane) return;

        // If already aborting, don't re-abort
        if (_laneChange.IsAborting) return;

        // Leave the target lane registration from initiation (the adjacent point, not the end point)
        if (_laneChangeTargetRegistrationPointId >= 0)
        {
            _spline.SlowestAiStates.Leave(_laneChangeTargetRegistrationPointId, this);
            _laneChangeTargetRegistrationPointId = -1;
        }

        // Capture current interpolated position/tangent for smooth return
        // Use blended Y to match the car's actual visual height (not CatmullRom Y)
        Vector3 currentPosition = _laneChange.GetInterpolatedPosition();
        float currentBlendedY = _laneChange.GetBlendedHeight();
        currentPosition = currentPosition with { Y = currentBlendedY };
        Vector3 currentTangent = _laneChange.GetInterpolatedTangent();

        // Find a return point on the source lane ahead of us
        int sourcePointId = _laneChange.SourcePointId;
        if (sourcePointId < 0)
        {
            // Fallback: hard abort if we can't find source lane
            HardAbortLaneChange();
            return;
        }

        var points = _spline.Points;
        float returnDistance = Vector3.Distance(currentPosition, points[sourcePointId].Position);
        float minReturnDistance = Math.Max(returnDistance, _configuration.LaneChangeMinDistanceMeters * 0.5f);

        // Walk ahead in source lane to find a good return point
        float distanceAhead = 0;
        int returnPointId = sourcePointId;
        while (distanceAhead < minReturnDistance && returnPointId >= 0)
        {
            distanceAhead += points[returnPointId].Length;
            int nextId = points[returnPointId].NextId;
            if (nextId < 0) break;
            returnPointId = nextId;
        }

        if (returnPointId < 0)
        {
            HardAbortLaneChange();
            return;
        }

        Vector3 returnPosition = points[returnPointId].Position;
        Vector3 returnTangent = _spline.GetForwardVector(returnPointId);
        float returnCamber = _spline.Operations.GetCamber(returnPointId);

        _indicator = 0;

        _laneChange.BeginAbortReturn(
            currentPosition,
            currentTangent,
            returnPosition,
            returnTangent,
            returnPointId,
            returnCamber,
            sourcePointId,
            _spline.Points);

        Log.Verbose("AI {SessionId} smoothly aborting lane change, returning to source lane", EntryCarAi.EntryCar.SessionId);
    }

    private void HardAbortLaneChange()
    {
        if (_laneChangeSourcePointId >= 0)
        {
            _spline.SlowestAiStates.Leave(_laneChangeSourcePointId, this);
            _laneChangeSourcePointId = -1;
        }

        _junctionEvaluator.Clear();
        _indicator = 0;
        _laneChange.Abort();

        Log.Verbose("AI {SessionId} hard-aborted lane change", EntryCarAi.EntryCar.SessionId);
    }

    private void FinalizeAbortReturn()
    {
        if (!_laneChange.IsAborting) return;

        int returnPointId = _laneChange.TargetPointId;

        // Clean up source point tracking
        if (_laneChangeSourcePointId >= 0)
        {
            _spline.SlowestAiStates.Leave(_laneChangeSourcePointId, this);
            _laneChangeSourcePointId = -1;
        }

        // Update current point to where we returned in the source lane
        _spline.SlowestAiStates.Leave(_currentSplinePointId, this);
        _currentSplinePointId = returnPointId;
        _spline.SlowestAiStates.Enter(returnPointId, this);

        // Reset junction evaluator for clean state
        _junctionEvaluator.Clear();

        // Recalculate tangents for source lane
        if (!_junctionEvaluator.TryNext(CurrentSplinePointId, out var nextPointId))
        {
            Log.Debug("Car {SessionId} cannot find next point after abort return, despawning", EntryCarAi.EntryCar.SessionId);
            _laneChange.Complete();
            Despawn();
            return;
        }

        _currentVecLength = (_spline.Points[nextPointId].Position - _spline.Points[CurrentSplinePointId].Position).Length();
        _currentVecProgress = 0;
        CalculateTangents();

        _laneChange.Complete();

        Log.Verbose("AI {SessionId} completed abort return to source lane at point {ReturnPoint}", EntryCarAi.EntryCar.SessionId, returnPointId);
    }

    /// <returns>0 is the rear <br/> Angle is counterclockwise</returns>
    public float GetAngleToCar(CarStatus car)
    {
        float challengedAngle = (float) (Math.Atan2(Status.Position.X - car.Position.X, Status.Position.Z - car.Position.Z) * 180 / Math.PI);
        if (challengedAngle < 0)
            challengedAngle += 360;
        float challengedRot = Status.GetRotationAngle();

        challengedAngle += challengedRot;
        challengedAngle %= 360;

        return challengedAngle;
    }

    public void Update()
    {
        if (!Initialized)
            return;

        var ops = _spline.Operations;

        long currentTime = _sessionManager.ServerTimeMilliseconds;
        long dt = currentTime - _lastTick;
        _lastTick = currentTime;
        float dtSeconds = dt / 1000.0f;

        // Jerk limiting (Change 6B): smooth acceleration transitions
        const float maxJerk = 5.0f; // m/s^3
        float maxAccelChange = maxJerk * dtSeconds;
        Acceleration = Math.Clamp(Acceleration,
            _previousAcceleration - maxAccelChange,
            _previousAcceleration + maxAccelChange);
        _previousAcceleration = Acceleration;

        // Coasting behavior (Change 6A): light deceleration = engine braking only
        const float coastingThreshold = -1.5f; // m/s^2
        float effectiveAcceleration = Acceleration;
        if (Acceleration > coastingThreshold && Acceleration < 0)
        {
            // Coast: limit deceleration to engine braking rate
            effectiveAcceleration = Math.Max(Acceleration, coastingThreshold);
        }

        float moveMeters;
        if (effectiveAcceleration < 0 && CurrentSpeed > 0)
        {
            // Ballistic stop (Change 5C): prevent negative velocity
            float stoppingTime = -CurrentSpeed / effectiveAcceleration;

            if (stoppingTime < dtSeconds)
            {
                // Would stop within this frame - calculate exact partial distance
                moveMeters = CurrentSpeed * stoppingTime + 0.5f * effectiveAcceleration * stoppingTime * stoppingTime;
                CurrentSpeed = 0;
                Acceleration = 0;
                _previousAcceleration = 0;
            }
            else
            {
                CurrentSpeed += effectiveAcceleration * dtSeconds;
                moveMeters = dtSeconds * CurrentSpeed;
            }
        }
        else
        {
            if (effectiveAcceleration != 0)
            {
                CurrentSpeed += effectiveAcceleration * dtSeconds;

                if ((effectiveAcceleration < 0 && CurrentSpeed < TargetSpeed) || (effectiveAcceleration > 0 && CurrentSpeed > TargetSpeed))
                {
                    CurrentSpeed = TargetSpeed;
                    Acceleration = 0;
                    _previousAcceleration = 0;
                }
            }
            moveMeters = dtSeconds * CurrentSpeed;
        }

        // Clamp speed to never exceed desired max and never go negative
        CurrentSpeed = Math.Clamp(CurrentSpeed, 0, MaxSpeed);

        Vector3 smoothPosition;
        Vector3 smoothTangent;
        float camber;
        bool wasLaneChanging = _laneChange.IsChangingLane;

        if (_laneChange.IsChangingLane)
        {
            // Lane change movement
            _laneChange.UpdateProgress(moveMeters);

            var laneChangePoint = _laneChange.GetInterpolatedPoint();
            // Use terrain-following blended height from both lane profiles
            float blendedY = _laneChange.GetBlendedHeight();
            smoothPosition = new Vector3(laneChangePoint.Position.X, blendedY, laneChangePoint.Position.Z);
            camber = _laneChange.GetBlendedCamber();

            // Correct tangent Y: replace CatmullRom curve Y derivative with blended height slope
            // so that Status.Velocity matches actual position trajectory (fixes client-side drift)
            float blendedSlope = _laneChange.GetBlendedPitchSlope();
            var rawTangent = laneChangePoint.Tangent;
            float horizLen = MathF.Sqrt(rawTangent.X * rawTangent.X + rawTangent.Z * rawTangent.Z);
            smoothTangent = horizLen > 0.001f
                ? Vector3.Normalize(new Vector3(rawTangent.X, blendedSlope * horizLen, rawTangent.Z))
                : rawTangent;

            // Check if lane change is complete
            if (_laneChange.IsComplete)
            {
                if (_laneChange.IsAborting)
                    FinalizeAbortReturn();
                else
                    CompleteLaneChange();
            }
        }
        else
        {
            // Normal spline movement
            if (!Move(_currentVecProgress + moveMeters) || !_junctionEvaluator.TryNext(CurrentSplinePointId, out var nextPoint))
            {
                Log.Debug("Car {SessionId} reached spline end, despawning", EntryCarAi.EntryCar.SessionId);
                Despawn();
                return;
            }

            float t = _currentVecProgress / _currentVecLength;

            CatmullRom.CatmullRomPoint smoothPos = CatmullRom.Evaluate(ops.Points[CurrentSplinePointId].Position,
                ops.Points[nextPoint].Position,
                _startTangent,
                _endTangent,
                t);

            // Override Y with linear interpolation to prevent floating on inclines/declines
            float linearY = ops.Points[CurrentSplinePointId].Position.Y * (1 - t)
                          + ops.Points[nextPoint].Position.Y * t;
            smoothPosition = new Vector3(smoothPos.Position.X, linearY, smoothPos.Position.Z);
            smoothTangent = smoothPos.Tangent;
            camber = ops.GetCamber(CurrentSplinePointId, t);
        }

        // Yaw from CatmullRom tangent (horizontal direction is correct)
        float yaw = MathF.Atan2(smoothTangent.Z, smoothTangent.X) - MathF.PI / 2;

        // Pitch from spline tangent Y components for correct road slope angle
        float pitch;
        if (wasLaneChanging)
        {
            // During lane change (including completion frame), derive pitch from corrected tangent
            float lcHorizLength = new Vector2(smoothTangent.X, smoothTangent.Z).Length();
            pitch = MathF.Atan2(smoothTangent.Y, lcHorizLength);
        }
        else if (_junctionEvaluator.TryNext(CurrentSplinePointId, out var pitchNextPoint))
        {
            // Pitch from actual segment slopes to match linear Y interpolation
            float t2 = _currentVecProgress / _currentVecLength;

            // Current segment slope: CurrentSplinePointId → pitchNextPoint
            float curDy = ops.Points[pitchNextPoint].Position.Y - ops.Points[CurrentSplinePointId].Position.Y;
            float curDxz = MathF.Sqrt(
                MathF.Pow(ops.Points[pitchNextPoint].Position.X - ops.Points[CurrentSplinePointId].Position.X, 2) +
                MathF.Pow(ops.Points[pitchNextPoint].Position.Z - ops.Points[CurrentSplinePointId].Position.Z, 2));
            float currentSegPitch = MathF.Atan2(curDy, curDxz);

            // Interpolate toward next segment slope for smooth transition at boundary
            if (_junctionEvaluator.TryNext(pitchNextPoint, out var nextNextPoint))
            {
                float nextDy = ops.Points[nextNextPoint].Position.Y - ops.Points[pitchNextPoint].Position.Y;
                float nextDxz = MathF.Sqrt(
                    MathF.Pow(ops.Points[nextNextPoint].Position.X - ops.Points[pitchNextPoint].Position.X, 2) +
                    MathF.Pow(ops.Points[nextNextPoint].Position.Z - ops.Points[pitchNextPoint].Position.Z, 2));
                float nextSegPitch = MathF.Atan2(nextDy, nextDxz);
                pitch = currentSegPitch + (nextSegPitch - currentSegPitch) * t2;
            }
            else
            {
                pitch = currentSegPitch;
            }
        }
        else
        {
            pitch = 0;
        }

        Vector3 rotation = new Vector3
        {
            X = yaw,
            Y = pitch,
            Z = camber
        };

        float tyreAngularSpeed = GetTyreAngularSpeed(CurrentSpeed, EntryCarAi.TyreDiameterMeters);
        byte encodedTyreAngularSpeed =  (byte) (Math.Clamp(MathF.Round(MathF.Log10(tyreAngularSpeed + 1.0f) * 20.0f) * Math.Sign(tyreAngularSpeed), -100.0f, 154.0f) + 100.0f);

        Status.Timestamp = _sessionManager.ServerTimeMilliseconds;
        Status.Position = smoothPosition with { Y = smoothPosition.Y + EntryCarAi.AiSplineHeightOffsetMeters };
        Status.Rotation = rotation;
        Status.Velocity = smoothTangent * CurrentSpeed;
        Status.SteerAngle = 127;
        Status.WheelAngle = 127;
        Status.TyreAngularSpeed[0] = encodedTyreAngularSpeed;
        Status.TyreAngularSpeed[1] = encodedTyreAngularSpeed;
        Status.TyreAngularSpeed[2] = encodedTyreAngularSpeed;
        Status.TyreAngularSpeed[3] = encodedTyreAngularSpeed;
        Status.EngineRpm = (ushort)MathUtils.Lerp(EntryCarAi.AiIdleEngineRpm, EntryCarAi.AiMaxEngineRpm, CurrentSpeed / _configuration.MaxSpeedMs);
        Status.StatusFlag = GetLights(_configuration.EnableDaytimeLights, _weatherManager.CurrentSunPosition, _randomTwilight)
                            | (_sessionManager.ServerTimeMilliseconds < _stoppedForCollisionUntil || CurrentSpeed < 20 / 3.6f ? CarStatusFlags.HazardsOn : 0)
                            | (CurrentSpeed == 0 || Acceleration < -1.0f ? CarStatusFlags.BrakeLightsOn : 0)
                            | (_stoppedForObstacle && _sessionManager.ServerTimeMilliseconds > _obstacleHonkStart && _sessionManager.ServerTimeMilliseconds < _obstacleHonkEnd ? CarStatusFlags.Horn : 0)
                            | GetWiperSpeed(_weatherManager.CurrentWeather.RainIntensity)
                            | _indicator;
        Status.Gear = 2;
    }
        
    private static float GetTyreAngularSpeed(float speed, float wheelDiameter)
    {
        return speed / (MathF.PI * wheelDiameter) * 6;
    }

    private static CarStatusFlags GetWiperSpeed(float rainIntensity)
    {
        return rainIntensity switch
        {
            < 0.05f => 0,
            < 0.25f => CarStatusFlags.WiperLevel1,
            < 0.5f => CarStatusFlags.WiperLevel2,
            _ => CarStatusFlags.WiperLevel3
        };
    }
    
    private static CarStatusFlags GetLights(bool daytimeLights, SunPosition? sunPosition, double twilight)
    {
        const CarStatusFlags lightFlags = CarStatusFlags.LightsOn | CarStatusFlags.HighBeamsOff;
        if (daytimeLights || sunPosition == null) return lightFlags;

        return sunPosition.Value.Altitude < twilight ? lightFlags : 0;
    }
}
