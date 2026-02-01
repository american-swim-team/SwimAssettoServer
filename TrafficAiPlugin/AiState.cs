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

    // Lane change state
    private readonly LaneChangeState _laneChange = new();
    private long _lastLaneChangeAttempt;
    private long _laneChangeCompletedAt;
    private int _laneChangeSourcePointId = -1; // For dual SlowestAiStates tracking during lane change

    private readonly ACServerConfiguration _serverConfiguration;
    private readonly TrafficAiConfiguration _configuration;
    private readonly SessionManager _sessionManager;
    private readonly EntryCarManager _entryCarManager;
    private readonly WeatherManager _weatherManager;
    private readonly AiSpline _spline;
    private readonly TrafficAi _trafficAi;
    private readonly JunctionEvaluator _junctionEvaluator;

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
        TrafficAi trafficAi)
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
        SetRandomColor();

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
        _laneChangeCompletedAt = 0;
        _laneChangeSourcePointId = -1;
        if (_laneChange.IsChangingLane)
        {
            _laneChange.Abort();
        }
        _minObstacleDistance = Random.Shared.Next(8, 13);
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

    private (AiState? ClosestAiState, float ClosestAiStateDistance, float MaxSpeed) SplineLookahead()
    {
        var points = _spline.Points;
        var junctions = _spline.Junctions;
        
        float maxBrakingDistance = PhysicsUtils.CalculateBrakingDistance(CurrentSpeed, EntryCarAi.AiDeceleration) * 2 + 20;
        AiState? closestAiState = null;
        float closestAiStateDistance = float.MaxValue;
        bool junctionFound = false;
        float distanceTravelled = 0;
        var pointId = CurrentSplinePointId;
        ref readonly var point = ref points[pointId]; 
        float maxSpeed = float.MaxValue;
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
                
                var indicator = _junctionEvaluator.WillTakeJunction(point.JunctionStartId) ? jct.IndicateWhenTaken : jct.IndicateWhenNotTaken;
                if (indicator != 0 && !_junctionUnsafe && CanUseJunction(indicator))
                {
                    _indicator = indicator;
                    _nextJunctionId = point.JunctionStartId;
                    junctionFound = true;
                } else {
                    _junctionUnsafe = true;
                }
            }

            if (closestAiState == null)
            {
                var slowest = _spline.SlowestAiStates[pointId];

                if (slowest != null)
                {
                    closestAiState = slowest;
                    closestAiStateDistance = MathF.Max(0, Vector3.Distance(Status.Position, closestAiState.Status.Position)
                                                          - EntryCarAi.VehicleLengthPreMeters
                                                          - closestAiState.EntryCarAi.VehicleLengthPostMeters);
                }
            }

            float maxCorneringSpeedSquared = PhysicsUtils.CalculateMaxCorneringSpeedSquared(point.Radius, EntryCarAi.AiCorneringSpeedFactor);
            if (maxCorneringSpeedSquared < currentSpeedSquared)
            {
                float maxCorneringSpeed = MathF.Sqrt(maxCorneringSpeedSquared);
                float brakingDistance = PhysicsUtils.CalculateBrakingDistance(CurrentSpeed - maxCorneringSpeed,
                                            EntryCarAi.AiDeceleration * EntryCarAi.AiCorneringBrakeForceFactor)
                                        * EntryCarAi.AiCorneringBrakeDistanceFactor;

                if (brakingDistance > distanceTravelled)
                {
                    maxSpeed = Math.Min(maxCorneringSpeed, maxSpeed);
                }
            }
        }

        return (closestAiState, closestAiStateDistance, maxSpeed);
    }

        
    private bool CanUseJunction(CarStatusFlags indicator)
    {
        var ignorePlayer = ShouldIgnorePlayerObstacles();
        float boxWidth = _configuration.LaneWidthMeters + 1;
        float boxLength = _configuration.MaxAiSafetyDistanceMeters + 2;
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

            if (!ignorePlayer && car.Client?.HasSentFirstUpdate == true) {
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
                if (playerCar.Client?.HasSentFirstUpdate == true)
                {
                    float distance = Vector3.DistanceSquared(playerCar.Status.Position, Status.Position);

                    if (distance < minDistance
                        && Math.Abs(playerCar.Status.Position.Y - Status.Position.Y) < 1.5
                        && GetAngleToCar(playerCar.Status) is > 166 and < 194)
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
            
        if (_sessionManager.ServerTimeMilliseconds < _ignoreObstaclesUntil)
        {
            SetTargetSpeed(MaxSpeed);
            return;
        }

        if (_sessionManager.ServerTimeMilliseconds < _stoppedForCollisionUntil)
        {
            SetTargetSpeed(0);
            return;
        }
            
        float targetSpeed = InitialMaxSpeed;
        float maxSpeed = InitialMaxSpeed;
        bool hasObstacle = false;

        var splineLookahead = SplineLookahead();
        var playerObstacle = FindClosestPlayerObstacle();

        ClosestAiObstacleDistance = splineLookahead.ClosestAiState != null ? splineLookahead.ClosestAiStateDistance : -1;

        // Consider lane change if blocked by slower traffic
        if (!_laneChange.IsChangingLane)
        {
            ConsiderLaneChange(splineLookahead.ClosestAiState, splineLookahead.ClosestAiStateDistance);
        }

        if (playerObstacle.distance < _minObstacleDistance || splineLookahead.ClosestAiStateDistance < _minObstacleDistance)
        {
            targetSpeed = 0;
            hasObstacle = true;
        }
        else if (playerObstacle.distance < splineLookahead.ClosestAiStateDistance && playerObstacle.entryCar != null)
        {
            float playerSpeed = playerObstacle.entryCar.Status.Velocity.Length();

            if (playerSpeed < 0.1f)
            {
                playerSpeed = 0;
            }

            if ((playerSpeed < CurrentSpeed || playerSpeed == 0)
                && playerObstacle.distance < PhysicsUtils.CalculateBrakingDistance(CurrentSpeed - playerSpeed, EntryCarAi.AiDeceleration) * 2 + 20)
            {
                targetSpeed = Math.Max(WalkingSpeed, playerSpeed);
                hasObstacle = true;
            }
        }
        else if (splineLookahead.ClosestAiState != null)
        {
            float closestTargetSpeed = Math.Min(splineLookahead.ClosestAiState.CurrentSpeed, splineLookahead.ClosestAiState.TargetSpeed);
            if ((closestTargetSpeed < CurrentSpeed || splineLookahead.ClosestAiState.CurrentSpeed == 0)
                && splineLookahead.ClosestAiStateDistance < PhysicsUtils.CalculateBrakingDistance(CurrentSpeed - closestTargetSpeed, EntryCarAi.AiDeceleration) * 2 + 20)
            {
                targetSpeed = Math.Max(WalkingSpeed, closestTargetSpeed);
                hasObstacle = true;
            }
        }

        targetSpeed = Math.Min(splineLookahead.MaxSpeed, targetSpeed);

        if (CurrentSpeed == 0 && !_stoppedForObstacle)
        {
            _stoppedForObstacle = true;
            _stoppedForObstacleSince = _sessionManager.ServerTimeMilliseconds;
            _obstacleHonkStart = _stoppedForObstacleSince + Random.Shared.Next(3000, 7000);
            _obstacleHonkEnd = _obstacleHonkStart + Random.Shared.Next(500, 1500);
            Log.Verbose("AI {SessionId} stopped for obstacle", EntryCarAi.EntryCar.SessionId);
        }
        else if (CurrentSpeed > 0 && _stoppedForObstacle)
        {
            _stoppedForObstacle = false;
            Log.Verbose("AI {SessionId} no longer stopped for obstacle", EntryCarAi.EntryCar.SessionId);
        }
        else if (_stoppedForObstacle && _sessionManager.ServerTimeMilliseconds - _stoppedForObstacleSince > _configuration.IgnoreObstaclesAfterMilliseconds)
        {
            _ignoreObstaclesUntil = _sessionManager.ServerTimeMilliseconds + 10_000;
            Log.Verbose("AI {SessionId} ignoring obstacles until {IgnoreObstaclesUntil}", EntryCarAi.EntryCar.SessionId, _ignoreObstaclesUntil);
        }

        float deceleration = EntryCarAi.AiDeceleration;
        if (!hasObstacle)
        {
            deceleration *= EntryCarAi.AiCorneringBrakeForceFactor;
        }
        
        MaxSpeed = maxSpeed;
        SetTargetSpeed(targetSpeed, deceleration, EntryCarAi.AiAcceleration);
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

        // Look ahead in target lane - check for slower AI blocking
        float distanceAhead = 0;
        int checkPointId = targetPointId;
        while (distanceAhead < lookAheadDistance && checkPointId >= 0)
        {
            var slowest = _spline.SlowestAiStates[checkPointId];
            if (slowest != null && slowest != this)
            {
                // Someone is ahead in target lane
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
                // Someone is behind in target lane
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

        // Check for players in target lane area
        if (!ShouldIgnorePlayerObstacles())
        {
            var targetPosition = targetPoint.Position;
            foreach (var car in _entryCarManager.EntryCars)
            {
                if (car.Client?.HasSentFirstUpdate == true)
                {
                    Vector3 futurePosition = car.Status.Position + car.Status.Velocity * timeHorizon;
                    float distanceSquared = Vector3.DistanceSquared(futurePosition, targetPosition);
                    if (distanceSquared < _configuration.LaneWidthMeters * _configuration.LaneWidthMeters * 4)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private void ConsiderLaneChange(AiState? closestObstacle, float obstacleDistance)
    {
        if (!_configuration.EnableLaneChanging) return;
        if (_laneChange.IsChangingLane) return;
        if (_nextJunctionId >= 0) return; // Don't lane change during junction approach

        long currentTime = _sessionManager.ServerTimeMilliseconds;

        // Check cooldown
        if (currentTime - _lastLaneChangeAttempt < _configuration.LaneChangeCooldownMilliseconds)
            return;

        var points = _spline.Points;
        ref readonly var currentPoint = ref points[CurrentSplinePointId];

        // Trigger 1: Overtake slower traffic ahead (move to left/fast lane)
        if (closestObstacle != null && obstacleDistance < _configuration.LaneChangeMaxDistanceMeters * _configuration.LaneChangeOvertakeTriggerMultiplier)
        {
            float speedDiff = CurrentSpeed - Math.Min(closestObstacle.CurrentSpeed, closestObstacle.TargetSpeed);
            if (speedDiff > _configuration.LaneChangeSpeedThresholdMs && currentPoint.LeftId >= 0)
            {
                _lastLaneChangeAttempt = currentTime;
                if (TryInitiateLaneChange(currentPoint.LeftId, true))
                    return;
            }
        }

        // Trigger 2: Return to slow lane after overtake (move to right lane)
        if (currentPoint.RightId >= 0 && currentTime - _laneChangeCompletedAt > _configuration.LaneChangeCooldownMilliseconds * 2)
        {
            // Return right if we have no obstacle ahead or obstacle is far away
            if (closestObstacle == null || obstacleDistance > _configuration.LaneChangeMaxDistanceMeters * _configuration.LaneChangeReturnToRightMultiplier)
            {
                _lastLaneChangeAttempt = currentTime;
                if (TryInitiateLaneChange(currentPoint.RightId, false))
                    return;
            }
        }

        // Trigger 3: Small probability for spontaneous lane change for variety (prefer right/slow lane)
        if (Random.Shared.NextDouble() < _configuration.LaneChangeSpontaneousProbability && closestObstacle == null)
        {
            int targetLane = currentPoint.RightId >= 0 ? currentPoint.RightId : currentPoint.LeftId;
            if (targetLane >= 0)
            {
                _lastLaneChangeAttempt = currentTime;
                TryInitiateLaneChange(targetLane, currentPoint.LeftId >= 0 && targetLane == currentPoint.LeftId);
            }
        }
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
        Vector3 endPosition = endPoint.Position;

        // Tangents: current direction and target lane direction, scaled by distance
        Vector3 startTangent = Vector3.Normalize(Status.Velocity.Length() > 0.1f ? Status.Velocity : _spline.GetForwardVector(CurrentSplinePointId)) * laneChangeDistance * 0.5f;
        Vector3 endTangent = _spline.GetForwardVector(endPointId) * laneChangeDistance * 0.5f;

        // Get camber values for interpolation
        float startCamber = _spline.Operations.GetCamber(CurrentSplinePointId);
        float endCamber = _spline.Operations.GetCamber(endPointId);

        // Set indicator
        _indicator = isOvertake ? CarStatusFlags.IndicateLeft : CarStatusFlags.IndicateRight;

        // Register in target lane for collision avoidance (source lane already tracked via CurrentSplinePointId)
        _laneChangeSourcePointId = CurrentSplinePointId;
        _spline.SlowestAiStates.Enter(targetPointId, this);

        // Begin lane change
        _laneChange.Begin(
            CurrentSplinePointId,
            endPointId,
            startPosition,
            endPosition,
            startTangent,
            endTangent,
            startCamber,
            endCamber,
            isOvertake);

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

        _laneChangeCompletedAt = _sessionManager.ServerTimeMilliseconds;
        _laneChange.Complete();

        Log.Verbose("AI {SessionId} completed lane change to point {TargetPoint}", EntryCarAi.EntryCar.SessionId, targetPointId);
    }

    private void AbortLaneChange()
    {
        if (!_laneChange.IsChangingLane) return;

        // Leave the target lane registration we made when initiating
        if (_laneChange.TargetPointId >= 0)
        {
            _spline.SlowestAiStates.Leave(_laneChange.TargetPointId, this);
        }

        // Clean up source point tracking
        if (_laneChangeSourcePointId >= 0)
        {
            // Leave the source point and re-enter current point
            _spline.SlowestAiStates.Leave(_laneChangeSourcePointId, this);
            _laneChangeSourcePointId = -1;
        }

        _indicator = 0;
        _laneChange.Abort();

        Log.Verbose("AI {SessionId} aborted lane change", EntryCarAi.EntryCar.SessionId);
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

    private void SetTargetSpeed(float speed, float deceleration, float acceleration)
    {
        TargetSpeed = speed;
        if (speed < CurrentSpeed)
        {
            Acceleration = -deceleration;
        }
        else if (speed > CurrentSpeed)
        {
            Acceleration = acceleration;
        }
        else
        {
            Acceleration = 0;
        }
    }

    private void SetTargetSpeed(float speed)
    {
        SetTargetSpeed(speed, EntryCarAi.AiDeceleration, EntryCarAi.AiAcceleration);
    }

    public void Update()
    {
        if (!Initialized)
            return;

        var ops = _spline.Operations;

        long currentTime = _sessionManager.ServerTimeMilliseconds;
        long dt = currentTime - _lastTick;
        _lastTick = currentTime;

        if (Acceleration != 0)
        {
            CurrentSpeed += Acceleration * (dt / 1000.0f);

            if ((Acceleration < 0 && CurrentSpeed < TargetSpeed) || (Acceleration > 0 && CurrentSpeed > TargetSpeed))
            {
                CurrentSpeed = TargetSpeed;
                Acceleration = 0;
            }
        }

        float moveMeters = (dt / 1000.0f) * CurrentSpeed;

        Vector3 smoothPosition;
        Vector3 smoothTangent;
        float camber;

        if (_laneChange.IsChangingLane)
        {
            // Lane change movement
            _laneChange.UpdateProgress(moveMeters);

            var laneChangePoint = _laneChange.GetInterpolatedPoint();
            smoothPosition = laneChangePoint.Position;
            smoothTangent = laneChangePoint.Tangent;
            camber = _laneChange.GetInterpolatedCamber();

            // Check if lane change is complete
            if (_laneChange.IsComplete)
            {
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

            CatmullRom.CatmullRomPoint smoothPos = CatmullRom.Evaluate(ops.Points[CurrentSplinePointId].Position,
                ops.Points[nextPoint].Position,
                _startTangent,
                _endTangent,
                _currentVecProgress / _currentVecLength);

            smoothPosition = smoothPos.Position;
            smoothTangent = smoothPos.Tangent;
            camber = ops.GetCamber(CurrentSplinePointId, _currentVecProgress / _currentVecLength);
        }

        Vector3 rotation = new Vector3
        {
            X = MathF.Atan2(smoothTangent.Z, smoothTangent.X) - MathF.PI / 2,
            Y = (MathF.Atan2(new Vector2(smoothTangent.Z, smoothTangent.X).Length(), smoothTangent.Y) - MathF.PI / 2) * -1f,
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
                            | (CurrentSpeed == 0 || Acceleration < 0 ? CarStatusFlags.BrakeLightsOn : 0)
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
