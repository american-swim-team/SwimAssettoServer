using AssettoServer.Server.Plugin;
using AssettoServer.Server;
using AssettoServer.Shared.Network.Packets.Incoming;
using AssettoServer.Network.ClientMessages;
using AssettoServer.Network.Tcp;
using System.Numerics;
using Serilog;
using Microsoft.Extensions.Hosting;
using System.Reflection;
using TrafficAiPlugin.Shared;

namespace SwimCrashPlugin;

public class SwimCrashHandler : BackgroundService
{
    private readonly SwimCrashConfiguration config;
    private readonly SessionManager _sessionManager;
    private readonly EntryCarManager _entryCarManager;
    private readonly ITrafficAi? _trafficAi;
    private Dictionary<ulong, CarState> CarStates = new Dictionary<ulong, CarState>();

    // Collision events can arrive up to 5 seconds after the actual collision.
    // The rotation history buffer must be large enough to look back past that delay.
    private const int CollisionReportDelayMs = 5000;

    public SwimCrashHandler(SwimCrashConfiguration configuration, SessionManager sessionManager, EntryCarManager entryCarManager, CSPServerScriptProvider scriptProvider, ITrafficAi? trafficAi = null)
    {
        Log.Information("------------------------------------");
        Log.Information("SwimCrashPlugin");
        Log.Information("By Romedius");
        Log.Information("------------------------------------");

        using var streamReader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("SwimCrashPlugin.lua.swimcrash.lua")!);
        scriptProvider.AddScript(streamReader.ReadToEnd(), "swimcrash.lua");

        config = configuration;

        _sessionManager = sessionManager;
        _entryCarManager = entryCarManager;
        _trafficAi = trafficAi;
        _entryCarManager.ClientConnected += OnConnected;
        _entryCarManager.ClientDisconnected += OnDisconnected;
    }

    private void OnConnected(ACTcpClient sender, EventArgs e)
    {
        Log.Verbose("Client registered for collision management");
        sender.Collision += OnCollision;
        sender.EntryCar.PositionUpdateReceived += OnPositionUpdateReceived;
        CarStates[sender.Guid] = new CarState();
    }

    private void OnDisconnected(ACTcpClient sender, EventArgs e)
    {
        Log.Verbose("Client unregistered for collision management");
        sender.Collision -= OnCollision;
        sender.EntryCar.PositionUpdateReceived -= OnPositionUpdateReceived;

        if (CarStates.TryGetValue(sender.Guid, out var state) && state.WaitingForReEnable)
        {
            sender.EntryCar.SetCollisions(true);
        }

        CarStates.Remove(sender.Guid);
    }

    private long HistoryWindowMs => (config.MonitorTime ?? 2500) + CollisionReportDelayMs;

    private (float X, float Y) ComputeAccumulatedRotation(CarState state, long windowMs)
    {
        long cutoff = _sessionManager.ServerTimeMilliseconds - windowMs;
        float accX = 0, accY = 0;
        foreach (var entry in state.RotationHistory)
        {
            if (entry.Timestamp >= cutoff)
            {
                accX += entry.DeltaX;
                accY += entry.DeltaY;
            }
        }
        return (accX, accY);
    }

    private void TriggerCrashReset(EntryCar entryCar, CarState state)
    {
        state.MonitorGrip = false;

        entryCar.SetCollisions(false);
        state.WaitingForReEnable = true;
        state.CollisionsDisabledTime = _sessionManager.ServerTimeMilliseconds;
        state.LastCollisionWhileWaiting = _sessionManager.ServerTimeMilliseconds;

        _entryCarManager.BroadcastPacket(new ResetCarPacket
        {
            Target = entryCar.Client!.SessionId
        });
        _trafficAi?.GetAiCarBySessionId(entryCar.SessionId).TryTeleportToSpline();
        Log.Information("Reset {client} due to spinning", entryCar.Client.Name);
    }

    private void OnCollision(ACTcpClient? sender, CollisionEventArgs e) {
        if (e.Speed < config.SpeedThreshold) {
            Log.Debug("SwimCrash: Collision below speed threshold ({Speed} < {Threshold}), sender={Sender}",
                e.Speed, config.SpeedThreshold, sender?.Name ?? "null");
            return;
        }

        EntryCar? targetEntryCar = null;
        ulong? targetGuid = null;

        if (sender == null) {
            if (e.TargetCar == null || e.TargetCar.Client == null) {
                Log.Verbose("No client found. Ignoring collision. No TargetCar and no sender.");
                return;
            }
            targetEntryCar = e.TargetCar;
            targetGuid = e.TargetCar.Client.Guid;
        } else {
            targetEntryCar = sender.EntryCar;
            targetGuid = sender.Guid;
        }

        var state = CarStates[targetGuid.Value];

        Log.Information("SwimCrash: Collision for {Client}, speed={Speed}, historyEntries={Count}, WaitingForReEnable={Waiting}",
            targetEntryCar.Client?.Name, e.Speed, state.RotationHistory.Count, state.WaitingForReEnable);

        if (state.WaitingForReEnable)
        {
            state.LastCollisionWhileWaiting = _sessionManager.ServerTimeMilliseconds;
            return;
        }

        // Check rotation history retroactively - the spin likely already happened
        // before this collision event was reported (up to 5s delay)
        var (accX, accY) = ComputeAccumulatedRotation(state, HistoryWindowMs);

        Log.Information("SwimCrash: Retroactive check for {Client}: accX={AccX:F3} accY={AccY:F3} (thresholds: spin={SpinT} flip={FlipT})",
            targetEntryCar.Client?.Name, accX, accY, config.SpinThreshold, config.FlipThreshold);

        if (accX > config.SpinThreshold || accY > config.FlipThreshold)
        {
            TriggerCrashReset(targetEntryCar, state);
            return;
        }

        // Spin hasn't happened yet (or was too small) - monitor going forward
        state.MonitorGrip = true;
        state.LastCollisionTime = _sessionManager.ServerTimeMilliseconds;
        Log.Information("SwimCrash: Forward monitoring started for {Client}", targetEntryCar.Client?.Name);
    }

    private static float WrapAngle(float delta)
    {
        while (delta > MathF.PI) delta -= 2 * MathF.PI;
        while (delta < -MathF.PI) delta += 2 * MathF.PI;
        return delta;
    }

    private void OnPositionUpdateReceived(EntryCar e, in PositionUpdateIn positionUpdate) {
        if (e.Client == null) {
            return;
        }

        var state = CarStates[e.Client.Guid];
        long now = _sessionManager.ServerTimeMilliseconds;

        // Always track rotation deltas into the rolling history buffer,
        // regardless of MonitorGrip state. This lets us look back retroactively
        // when a delayed collision event arrives.
        if (state.PreviousRotation != null)
        {
            float deltaX = MathF.Abs(WrapAngle(positionUpdate.Rotation.X - state.PreviousRotation.Value.X));
            float deltaY = MathF.Abs(WrapAngle(positionUpdate.Rotation.Y - state.PreviousRotation.Value.Y));
            state.RotationHistory.Enqueue((now, deltaX, deltaY));
        }
        state.PreviousRotation = positionUpdate.Rotation;

        // Trim entries older than the history window
        long historyCutoff = now - HistoryWindowMs;
        while (state.RotationHistory.Count > 0 && state.RotationHistory.Peek().Timestamp < historyCutoff)
        {
            state.RotationHistory.Dequeue();
        }

        // Handle re-enable logic
        if (state.WaitingForReEnable)
        {
            if (now - state.CollisionsDisabledTime > config.MaxNoCollisionTimeMs)
            {
                e.SetCollisions(true);
                state.WaitingForReEnable = false;
                Log.Information("Force re-enabled collisions for {client} (max time exceeded)", e.Client.Name);
                return;
            }

            if (positionUpdate.Velocity.Length() >= config.CruisingSpeedThreshold
                && now - state.LastCollisionWhileWaiting > config.ReEnableCooldownMs)
            {
                e.SetCollisions(true);
                state.WaitingForReEnable = false;
                Log.Information("Re-enabled collisions for {client} (cruising speed reached)", e.Client.Name);
            }

            return;
        }

        // Forward monitoring after collision event arrived
        if (!state.MonitorGrip) {
            return;
        }

        if (now - state.LastCollisionTime > config.MonitorTime) {
            var (finalAccX, finalAccY) = ComputeAccumulatedRotation(state, config.MonitorTime ?? 2500);
            Log.Information("SwimCrash: Monitoring timed out for {Client} without triggering. Final accX={AccX:F3} accY={AccY:F3}",
                e.Client.Name, finalAccX, finalAccY);
            state.MonitorGrip = false;
            return;
        }

        // Check accumulated rotation over the monitoring window
        var (accX, accY) = ComputeAccumulatedRotation(state, config.MonitorTime ?? 2500);

        Log.Debug("SwimCrash: Monitoring {Client}: accX={AccX:F3} accY={AccY:F3} rot=({RotX:F3},{RotY:F3},{RotZ:F3})",
            e.Client.Name, accX, accY, positionUpdate.Rotation.X, positionUpdate.Rotation.Y, positionUpdate.Rotation.Z);

        if (accX > config.SpinThreshold || accY > config.FlipThreshold)
        {
            TriggerCrashReset(e, state);
        }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.CompletedTask;
    }
}

class CarState {
    public long LastCollisionTime;
    public bool MonitorGrip;
    public Vector3? PreviousRotation;
    public readonly Queue<(long Timestamp, float DeltaX, float DeltaY)> RotationHistory = new();

    public bool WaitingForReEnable;
    public long CollisionsDisabledTime;
    public long LastCollisionWhileWaiting;
}
