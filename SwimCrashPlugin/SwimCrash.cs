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

    private void OnCollision(ACTcpClient? sender, CollisionEventArgs e) {
        if (e.Speed < config.SpeedThreshold) {
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

        if (state.WaitingForReEnable)
        {
            state.LastCollisionWhileWaiting = _sessionManager.ServerTimeMilliseconds;
            return;
        }

        state.MonitorGrip = true;
        state.LastCollisionTime = _sessionManager.ServerTimeMilliseconds;
        state.PreviousRotation = null;
        state.AccumulatedRotationX = 0;
        state.AccumulatedRotationY = 0;
    }

    private static float WrapAngle(float delta)
    {
        while (delta > MathF.PI) delta -= 2 * MathF.PI;
        while (delta < -MathF.PI) delta += 2 * MathF.PI;
        return delta;
    }

    private void OnPositionUpdateReceived(EntryCar e, in PositionUpdateIn positionUpdate) {
        if (e.Client == null) {
            Log.Verbose("No client found. Ignoring position update.");
            return;
        }

        var state = CarStates[e.Client.Guid];

        if (state.WaitingForReEnable)
        {
            if (_sessionManager.ServerTimeMilliseconds - state.CollisionsDisabledTime > config.MaxNoCollisionTimeMs)
            {
                e.SetCollisions(true);
                state.WaitingForReEnable = false;
                Log.Verbose("Force re-enabled collisions for {client} (max time exceeded)", e.Client.Name);
                return;
            }

            if (positionUpdate.Velocity.Length() >= config.CruisingSpeedThreshold
                && _sessionManager.ServerTimeMilliseconds - state.LastCollisionWhileWaiting > config.ReEnableCooldownMs)
            {
                e.SetCollisions(true);
                state.WaitingForReEnable = false;
                Log.Verbose("Re-enabled collisions for {client} (cruising speed reached)", e.Client.Name);
            }

            return;
        }

        if (!state.MonitorGrip) {
            return;
        }

        if (_sessionManager.ServerTimeMilliseconds - state.LastCollisionTime > config.MonitorTime) {
            state.MonitorGrip = false;
            state.PreviousRotation = null;
            state.AccumulatedRotationX = 0;
            state.AccumulatedRotationY = 0;
            return;
        }

        if (state.PreviousRotation == null) {
            state.PreviousRotation = positionUpdate.Rotation;
            return;
        }

        float deltaX = WrapAngle(positionUpdate.Rotation.X - state.PreviousRotation.Value.X);
        float deltaY = WrapAngle(positionUpdate.Rotation.Y - state.PreviousRotation.Value.Y);
        state.AccumulatedRotationX += MathF.Abs(deltaX);
        state.AccumulatedRotationY += MathF.Abs(deltaY);
        state.PreviousRotation = positionUpdate.Rotation;

        bool inSpin = state.AccumulatedRotationX > config.SpinThreshold;
        bool inFlip = state.AccumulatedRotationY > config.FlipThreshold;

        Log.Verbose("Accumulated rotation X={rotX} Y={rotY} (thresholds: spin={spinT} flip={flipT})",
            state.AccumulatedRotationX, state.AccumulatedRotationY, config.SpinThreshold, config.FlipThreshold);

        if (inSpin || inFlip)
        {
            state.MonitorGrip = false;
            state.PreviousRotation = null;
            state.AccumulatedRotationX = 0;
            state.AccumulatedRotationY = 0;

            e.SetCollisions(false);
            state.WaitingForReEnable = true;
            state.CollisionsDisabledTime = _sessionManager.ServerTimeMilliseconds;
            state.LastCollisionWhileWaiting = _sessionManager.ServerTimeMilliseconds;

            _entryCarManager.BroadcastPacket(new ResetCarPacket
            {
                Target = e.Client.SessionId
            });
            _trafficAi?.GetAiCarBySessionId(e.SessionId).TryTeleportToSpline();
            Log.Verbose("Reset {client} due to spinning", e.Client.Name);
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
    public float AccumulatedRotationX;
    public float AccumulatedRotationY;

    public bool WaitingForReEnable;
    public long CollisionsDisabledTime;
    public long LastCollisionWhileWaiting;
}
