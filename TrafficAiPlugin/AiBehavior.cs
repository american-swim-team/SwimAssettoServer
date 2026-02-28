using System.Numerics;
using System.Reflection;
using AssettoServer.Network.Http;
using AssettoServer.Network.Tcp;
using AssettoServer.Server;
using AssettoServer.Server.Configuration;
using AssettoServer.Shared.Network.Packets.Outgoing;
using AssettoServer.Utils;
using Microsoft.Extensions.Hosting;
using Prometheus;
using Serilog;
using TrafficAiPlugin.Configuration;
using TrafficAiPlugin.Splines;

namespace TrafficAiPlugin;

public class AiBehavior : BackgroundService
{
    private readonly ACServerConfiguration _serverConfiguration;
    private readonly TrafficAiConfiguration _configuration;
    private readonly SessionManager _sessionManager;
    private readonly EntryCarManager _entryCarManager;
    private readonly TrafficAi _trafficAi;
    private readonly AiSpline _spline;
    private readonly HttpInfoCache _httpInfoCache;

    private readonly JunctionEvaluator _junctionEvaluator;

    private readonly Gauge _aiStateCountMetric = Metrics.CreateGauge("assettoserver_aistatecount", "Number of AI states");
    private readonly Summary _updateDurationTimer = Metrics.CreateSummary("assettoserver_aibehavior_update",
        "AiBehavior.Update Duration",
        MetricDefaults.DefaultQuantiles);
    private readonly Summary _obstacleDetectionDurationTimer = Metrics.CreateSummary("assettoserver_aibehavior_obstacledetection",
        "AiBehavior.ObstacleDetection Duration",
        MetricDefaults.DefaultQuantiles);

    public AiBehavior(SessionManager sessionManager,
        ACServerConfiguration serverConfiguration,
        TrafficAiConfiguration configuration,
        EntryCarManager entryCarManager,
        CSPServerScriptProvider serverScriptProvider,
        TrafficAi trafficAi,
        AiSpline spline,
        HttpInfoCache httpInfoCache)
    {
        _sessionManager = sessionManager;
        _serverConfiguration = serverConfiguration;
        _configuration = configuration;
        _entryCarManager = entryCarManager;
        _trafficAi = trafficAi;
        _spline = spline;
        _httpInfoCache = httpInfoCache;
        _junctionEvaluator = new JunctionEvaluator(spline, false);

        if (_configuration.Debug)
        {
            using var aiDebugStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TrafficAiPlugin.lua.ai_debug.lua")!;
            serverScriptProvider.AddScript(aiDebugStream, "ai_debug.lua");
        }
        using var resetCarStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TrafficAiPlugin.lua.resetcar.lua")!;
        serverScriptProvider.AddScript(resetCarStream, "resetcar.lua");

        _entryCarManager.ClientConnected += (client, _) =>
        {
            client.ChecksumPassed += OnClientChecksumPassed;
            client.Collision += OnCollision;
        };
        _entryCarManager.ClientDisconnected += OnClientDisconnected;
        _configuration.PropertyChanged += (_, _) => AdjustOverbooking();

        _sessionManager.SessionChanged += OnSessionChanged;
    }

    private void OnCollision(ACTcpClient sender, CollisionEventArgs args)
    {
        if (args.TargetCar?.AiControlled == true)
        {
            var target = _trafficAi.GetAiCarBySessionId(args.TargetCar.SessionId);
            var targetAiState = target.GetClosestAiState(sender.EntryCar.Status.Position);
            if (targetAiState.AiState != null && targetAiState.DistanceSquared < 25 * 25)
            {
                // Capture spawn counter to validate AiState is still valid after delay
                var aiState = targetAiState.AiState;
                var spawnCounter = aiState.SpawnCounter;
                Task.Delay(Random.Shared.Next(100, 500)).ContinueWith(_ =>
                {
                    // Only call StopForCollision if the AiState hasn't been replaced/despawned
                    if (aiState.Initialized && aiState.SpawnCounter == spawnCounter)
                    {
                        aiState.StopForCollision();
                    }
                });
            }
        }
    }

    private void OnClientChecksumPassed(ACTcpClient sender, EventArgs args)
    {
        var entryCar = _trafficAi.GetAiCarBySessionId(sender.SessionId);
        entryCar.SetAiControl(false);
        AdjustOverbooking();
    }

    private async Task ObstacleDetectionAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var context = _obstacleDetectionDurationTimer.NewTimer();

                for (int i = 0; i < _entryCarManager.EntryCars.Length; i++)
                {
                    var entryCar = _entryCarManager.EntryCars[i];
                    if (entryCar.AiControlled)
                    {
                        var entryCarAi = _trafficAi.GetAiCarBySessionId(entryCar.SessionId);
                        entryCarAi.AiObstacleDetection();
                    }
                }

                if (_configuration.Debug)
                {
                    SendDebugPackets();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in AI obstacle detection");
            }
        }
    }

    private void SendDebugPackets()
    {
        var sessionIds = new byte[_entryCarManager.EntryCars.Length];
        var currentSpeeds = new byte[_entryCarManager.EntryCars.Length];
        var targetSpeeds = new byte[_entryCarManager.EntryCars.Length];
        var maxSpeeds = new byte[_entryCarManager.EntryCars.Length];
        var closestAiObstacles = new short[_entryCarManager.EntryCars.Length];

        foreach (var player in _entryCarManager.ConnectedCars.Values)
        {
            if (player.Client?.HasSentFirstUpdate == false) continue;

            var count = 0;
            foreach (var car in _entryCarManager.EntryCars)
            {
                if (!car.AiControlled) continue;

                var carAi = _trafficAi.GetAiCarBySessionId(car.SessionId);
                var (aiState, _) = carAi.GetClosestAiState(player.Status.Position);
                if (aiState == null) continue;

                sessionIds[count] = car.SessionId;
                currentSpeeds[count] = (byte)(aiState.CurrentSpeed * 3.6f);
                targetSpeeds[count] = (byte)(aiState.TargetSpeed * 3.6f);
                maxSpeeds[count] = (byte)(aiState.MaxSpeed * 3.6f);
                closestAiObstacles[count] = (short)aiState.ClosestAiObstacleDistance;
                count++;
            }

            for (int i = 0; i < count; i += AiDebugPacket.Length)
            {
                var packet = new AiDebugPacket();
                Array.Fill(packet.SessionIds, (byte)255);

                new ArraySegment<byte>(sessionIds, i, Math.Min(AiDebugPacket.Length, count - i)).CopyTo(packet.SessionIds);
                new ArraySegment<short>(closestAiObstacles, i, Math.Min(AiDebugPacket.Length, count - i)).CopyTo(packet.ClosestAiObstacles);
                new ArraySegment<byte>(currentSpeeds, i, Math.Min(AiDebugPacket.Length, count - i)).CopyTo(packet.CurrentSpeeds);
                new ArraySegment<byte>(maxSpeeds, i, Math.Min(AiDebugPacket.Length, count - i)).CopyTo(packet.MaxSpeeds);
                new ArraySegment<byte>(targetSpeeds, i, Math.Min(AiDebugPacket.Length, count - i)).CopyTo(packet.TargetSpeeds);

                player.Client?.SendPacket(packet);
            }
        }
    }

    private readonly List<AssettoServer.Server.EntryCar> _playerCars = new();
    private readonly List<AiState> _initializedAiStates = new();
    private readonly List<AiState> _uninitializedAiStates = new();
    private readonly List<Vector3> _playerOffsetPositions = new();
    private readonly List<KeyValuePair<AiState, float>> _aiMinDistanceToPlayer = new();
    private readonly List<KeyValuePair<AssettoServer.Server.EntryCar, float>> _playerMinDistanceToAi = new();
    private readonly List<PlayerCluster> _playerClusters = new();

    private struct PlayerCluster
    {
        public Vector3 Centroid;
        public readonly List<EntryCar> Players;
        public float RemainingBudget;

        public PlayerCluster(EntryCar firstPlayer, float budget)
        {
            Players = [firstPlayer];
            Centroid = firstPlayer.Status.Position;
            RemainingBudget = budget;
        }

        public void AddPlayer(EntryCar player, float additionalBudget)
        {
            Players.Add(player);
            // Recompute centroid as average of all player positions
            var sum = Vector3.Zero;
            for (int i = 0; i < Players.Count; i++)
                sum += Players[i].Status.Position;
            Centroid = sum / Players.Count;
            RemainingBudget += additionalBudget;
        }
    }

    private void Update()
    {
        using var context = _updateDurationTimer.NewTimer();

        _playerCars.Clear();
        _initializedAiStates.Clear();
        _uninitializedAiStates.Clear();
        _playerOffsetPositions.Clear();
        _aiMinDistanceToPlayer.Clear();
        _playerMinDistanceToAi.Clear();
        _playerClusters.Clear();

        foreach (var entryCar in _entryCarManager.EntryCars)
        {
            var (currentSplinePointId, _) = _spline.WorldToSpline(entryCar.Status.Position);
            var drivingTheRightWay = Vector3.Dot(_spline.Operations.GetForwardVector(currentSplinePointId), entryCar.Status.Velocity) > 0;

            if (!entryCar.AiControlled
                && entryCar.Client?.HasSentFirstUpdate == true
                && _sessionManager.ServerTimeMilliseconds - entryCar.LastActiveTime < _configuration.PlayerAfkTimeoutMilliseconds
                && (_configuration.TwoWayTraffic || _configuration.WrongWayTraffic || drivingTheRightWay))
            {
                _playerCars.Add(entryCar);
            }
            else if (entryCar.AiControlled)
            {
                var entryCarAi = _trafficAi.GetAiCarBySessionId(entryCar.SessionId);
                entryCarAi.RemoveUnsafeStates();
                entryCarAi.GetInitializedStates(_initializedAiStates, _uninitializedAiStates);
            }
        }

        _aiStateCountMetric.Set(_initializedAiStates.Count);

        if (_sessionManager.CurrentSession.StartTimeMilliseconds > _sessionManager.ServerTimeMilliseconds)
            return;

        for (int i = 0; i < _initializedAiStates.Count; i++)
        {
            _aiMinDistanceToPlayer.Add(new KeyValuePair<AiState, float>(_initializedAiStates[i], float.MaxValue));
        }

        for (int i = 0; i < _playerCars.Count; i++)
        {
            _playerMinDistanceToAi.Add(new KeyValuePair<AssettoServer.Server.EntryCar, float>(_playerCars[i], float.MaxValue));
        }

        // Get minimum distance to a player for each AI
        // Get minimum distance to AI for each player
        for (int i = 0; i < _initializedAiStates.Count; i++)
        {
            for (int j = 0; j < _playerCars.Count; j++)
            {
                if (_playerOffsetPositions.Count <= j)
                {
                    var offsetPosition = _playerCars[j].Status.Position;
                    if (_playerCars[j].Status.Velocity != Vector3.Zero)
                    {
                        offsetPosition += Vector3.Normalize(_playerCars[j].Status.Velocity) * _configuration.PlayerPositionOffsetMeters;
                    }

                    _playerOffsetPositions.Add(offsetPosition);
                }

                var distanceSquared = Vector3.DistanceSquared(_initializedAiStates[i].Status.Position, _playerOffsetPositions[j]);

                if (_aiMinDistanceToPlayer[i].Value > distanceSquared)
                {
                    _aiMinDistanceToPlayer[i] = new KeyValuePair<AiState, float>(_initializedAiStates[i], distanceSquared);
                }

                if (_playerMinDistanceToAi[j].Value > distanceSquared)
                {
                    _playerMinDistanceToAi[j] = new KeyValuePair<AssettoServer.Server.EntryCar, float>(_playerCars[j], distanceSquared);
                }
            }
        }

        // Order AI cars by their minimum distance to a player. Higher distance = higher chance for respawn
        _aiMinDistanceToPlayer.Sort((a, b) => b.Value.CompareTo(a.Value));

        foreach (var dist in _aiMinDistanceToPlayer)
        {
            if (dist.Value > _configuration.PlayerRadiusSquared
                && _sessionManager.ServerTimeMilliseconds > dist.Key.SpawnProtectionEnds)
            {
                _uninitializedAiStates.Add(dist.Key);
            }
        }

        // Build player clusters for spawn distribution
        float clusterRadiusSquared = _configuration.PlayerClusterRadiusMeters * _configuration.PlayerClusterRadiusMeters;
        float baseBudget = _configuration.AiPerPlayerTargetCount;
        float diminishing = _configuration.ClusterDiminishingFactor;

        for (int i = 0; i < _playerCars.Count; i++)
        {
            var player = _playerCars[i];
            bool addedToCluster = false;

            for (int c = 0; c < _playerClusters.Count; c++)
            {
                if (Vector3.DistanceSquared(player.Status.Position, _playerClusters[c].Centroid) < clusterRadiusSquared)
                {
                    var cluster = _playerClusters[c];
                    cluster.AddPlayer(player, baseBudget * diminishing);
                    _playerClusters[c] = cluster;
                    addedToCluster = true;
                    break;
                }
            }

            if (!addedToCluster)
            {
                _playerClusters.Add(new PlayerCluster(player, baseBudget));
            }
        }

        // Spawn loop using cluster budgets
        int maxAttempts = _uninitializedAiStates.Count * 2;
        int attempts = 0;
        while (_uninitializedAiStates.Count > 0 && _playerClusters.Count > 0 && attempts < maxAttempts)
        {
            attempts++;

            // Pick a cluster weighted by remaining budget
            float totalBudget = 0;
            for (int c = 0; c < _playerClusters.Count; c++)
                totalBudget += _playerClusters[c].RemainingBudget;

            if (totalBudget <= 0)
                break;

            float pick = (float)(Random.Shared.NextDouble() * totalBudget);
            int clusterIdx = 0;
            float cumulative = 0;
            for (int c = 0; c < _playerClusters.Count; c++)
            {
                cumulative += _playerClusters[c].RemainingBudget;
                if (pick <= cumulative)
                {
                    clusterIdx = c;
                    break;
                }
            }

            var selectedCluster = _playerClusters[clusterIdx];

            // Pick a random player from the cluster to use as spawn anchor
            var anchorPlayer = selectedCluster.Players[Random.Shared.Next(selectedCluster.Players.Count)];

            int spawnPointId = GetSpawnPoint(anchorPlayer);
            if (spawnPointId < 0 || !_junctionEvaluator.TryNext(spawnPointId, out _))
                continue;

            var previousAi = FindClosestAiState(spawnPointId, false);
            var nextAi = FindClosestAiState(spawnPointId, true);

            bool spawned = false;
            foreach (var targetAiState in _uninitializedAiStates)
            {
                if (!targetAiState.CanSpawn(spawnPointId, previousAi, nextAi))
                    continue;

                targetAiState.Teleport(spawnPointId);
                _uninitializedAiStates.Remove(targetAiState);
                spawned = true;
                break;
            }

            if (spawned)
            {
                // Decrement cluster budget
                selectedCluster.RemainingBudget -= 1.0f;
                _playerClusters[clusterIdx] = selectedCluster;

                // Remove exhausted clusters
                if (selectedCluster.RemainingBudget <= 0)
                {
                    _playerClusters.RemoveAt(clusterIdx);
                }
            }
        }
    }

    private AiState? FindClosestAiState(int pointId, bool forward)
    {
        var points = _spline.Points;
        float distanceTravelled = 0;
        float searchDistance = 50;
        ref readonly var point = ref points[pointId];

        AiState? closestAiState = null;
        while (distanceTravelled < searchDistance && closestAiState == null)
        {
            distanceTravelled += point.Length;
            // TODO reuse this junction evaluator for the newly spawned car
            pointId = forward ? _junctionEvaluator.Next(pointId) : _junctionEvaluator.Previous(pointId);
            if (pointId < 0)
                break;

            point = ref points[pointId];

            var slowest = _spline.SlowestAiStates[pointId];
            if (slowest != null)
            {
                closestAiState = slowest;
            }
        }

        return closestAiState;
    }

    private async Task UpdateAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_configuration.AiBehaviorUpdateIntervalMilliseconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                Update();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in AI update");
            }
        }
    }

    private void OnClientDisconnected(ACTcpClient sender, EventArgs args)
    {
        if (sender.EntryCar.AiMode != AiMode.None)
        {
            var entryCarAi = _trafficAi.GetAiCarBySessionId(sender.SessionId);
            entryCarAi.SetAiControl(true);
            AdjustOverbooking();
        }
    }

    private void OnSessionChanged(SessionManager sender, SessionChangedEventArgs args)
    {
        for (var i = 0; i < _initializedAiStates.Count; i++)
        {
            _initializedAiStates[i].Despawn();
        }
    }

    private int GetRandomWeighted(int max)
    {
        // Probabilities for max = 4
        // 0    4/10
        // 1    3/10
        // 2    2/10
        // 3    1/10

        int maxRand = max * (max + 1) / 2;
        int rand = Random.Shared.Next(maxRand);
        int target = 0;
        for (int i = max; i < maxRand; i += (i - 1))
        {
            if (rand < i) break;
            target++;
        }

        return target;
    }

    private bool IsPositionSafe(int pointId)
    {
        var ops = _spline.Operations;

        for (var i = 0; i < _entryCarManager.EntryCars.Length; i++)
        {
            var entryCar = _entryCarManager.EntryCars[i];
            var entryCarAi = _trafficAi.GetAiCarBySessionId(entryCar.SessionId);
            if (entryCar.AiControlled && !entryCarAi.IsPositionSafe(pointId))
            {
                return false;
            }

            if (entryCar.Client?.HasSentFirstUpdate == true
                && Vector3.DistanceSquared(entryCar.Status.Position, ops.Points[pointId].Position) < _configuration.SpawnSafetyDistanceToPlayerSquared)
            {
                return false;
            }
        }

        return true;
    }

    private int GetSpawnPoint(EntryCar playerCar)
    {
        var result = _spline.WorldToSpline(playerCar.Status.Position);
        var ops = _spline.Operations;


        if (result.PointId < 0 || ops.Points[result.PointId].NextId < 0) return -1;

        int direction = Vector3.Dot(ops.GetForwardVector(result.PointId), playerCar.Status.Velocity) > 0 ? 1 : -1;

        // Do not not spawn if a player is too far away from the AI spline, e.g. in pits or in a part of the map without traffic
        if (result.DistanceSquared > _configuration.MaxPlayerDistanceToAiSplineSquared)
        {
            return -1;
        }

        int minSpawnPoints, maxSpawnPoints;
#pragma warning disable CS0618 // Backward compat: check if legacy point-based params are set
        if (_configuration.MinSpawnDistanceMeters > 0 && _configuration.MaxSpawnDistanceMeters > 0
            && _configuration.MinSpawnDistancePoints == 0 && _configuration.MaxSpawnDistancePoints == 0)
#pragma warning restore CS0618
        {
            // New meter-based config: convert to points dynamically from player position
            minSpawnPoints = _spline.MetersToPoints(result.PointId, _configuration.MinSpawnDistanceMeters);
            maxSpawnPoints = _spline.MetersToPoints(result.PointId, _configuration.MaxSpawnDistanceMeters);
        }
        else
        {
            minSpawnPoints = _configuration.EffectiveMinSpawnDistancePoints;
            maxSpawnPoints = _configuration.EffectiveMaxSpawnDistancePoints;
        }

        if (maxSpawnPoints <= minSpawnPoints) maxSpawnPoints = minSpawnPoints + 1;
        int spawnDistance = Random.Shared.Next(minSpawnPoints, maxSpawnPoints);
        var spawnPointId = _junctionEvaluator.Traverse(result.PointId, spawnDistance * direction);

        if (spawnPointId >= 0)
        {
            spawnPointId = _spline.RandomLane(spawnPointId);
        }

        if (spawnPointId >= 0 && ops.Points[spawnPointId].NextId >= 0)
        {
            direction = Vector3.Dot(ops.GetForwardVector(spawnPointId), playerCar.Status.Velocity) > 0 ? 1 : -1;
        }

        while (spawnPointId >= 0 && !IsPositionSafe(spawnPointId))
        {
            spawnPointId = _junctionEvaluator.Traverse(spawnPointId, direction * 5);
        }

        if (spawnPointId >= 0)
        {
            spawnPointId = _spline.RandomLane(spawnPointId);
        }

        return spawnPointId;
    }

    private void AdjustOverbooking()
    {
        int playerCount = _entryCarManager.EntryCars.Count(car => car.Client != null && car.Client.IsConnected);
        var aiSlots = _entryCarManager.EntryCars.Where(car => car.Client == null && car.AiControlled).ToList(); // client null check is necessary here so that slots where someone is connecting don't count

        if (aiSlots.Count == 0)
        {
            Log.Debug("AI Slot overbooking update - no AI slots available");
            return;
        }

        int targetAiCount = Math.Min(playerCount * Math.Min((int)Math.Round(_configuration.AiPerPlayerTargetCount * _configuration.TrafficDensity), aiSlots.Count), _configuration.MaxAiTargetCount);

        int overbooking = targetAiCount / aiSlots.Count;
        int rest = targetAiCount % aiSlots.Count;

        Log.Debug("AI Slot overbooking update - No. players: {NumPlayers} - No. AI Slots: {NumAiSlots} - Target AI count: {TargetAiCount} - Overbooking: {Overbooking} - Rest: {Rest}",
            playerCount, aiSlots.Count, targetAiCount, overbooking, rest);

        for (int i = 0; i < aiSlots.Count; i++)
        {
            var entryCarAi = _trafficAi.GetAiCarBySessionId(aiSlots[i].SessionId);
            entryCarAi.SetAiOverbooking(i < rest ? overbooking + 1 : overbooking);
        }
    }

    private void SetHttpDetailsExtensions()
    {
        _httpInfoCache.Extensions.Add("aiTraffic", new Dictionary<string, List<byte>>
        {
            { "auto", _entryCarManager.EntryCars.Where(c => c.AiMode == AiMode.Auto).Select(c => c.SessionId).ToList() },
            { "fixed", _entryCarManager.EntryCars.Where(c => c.AiMode == AiMode.Fixed).Select(c => c.SessionId).ToList() }
        });
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        SetHttpDetailsExtensions();
        return Task.WhenAll(UpdateAsync(stoppingToken), ObstacleDetectionAsync(stoppingToken));
    }
}
