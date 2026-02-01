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

[OnlineEvent(Key = "noCollision")]
public class noCollision : OnlineEvent<noCollision>
{
    [OnlineEventField(Name = "entryCar")]
    public int EntryCar;
}

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
        CarStates.Remove(sender.Guid);
    }

    private void OnCollision(ACTcpClient? sender, CollisionEventArgs e) {
        if (e.Speed < config.SpeedThreshold) {
            return;
        }

        if (sender == null) {
            if (e.TargetCar == null || e.TargetCar.Client == null) {
                Log.Verbose("No client found. Ignoring collision. No TargetCar and no sender.");
                return;
            } else {
                var state = CarStates[e.TargetCar.Client.Guid];
                state.MonitorGrip = true;
                state.LastCollisionTime = _sessionManager.ServerTimeMilliseconds;
                CarStates[e.TargetCar.Client.Guid] = state;
            }
        } else {
            var state = CarStates[sender.Guid];
            state.MonitorGrip = true;
            state.LastCollisionTime = _sessionManager.ServerTimeMilliseconds;
            CarStates[sender.Guid] = state;
        }
    }

    private void OnPositionUpdateReceived(EntryCar e, in PositionUpdateIn positionUpdate) {
        if (e.Client == null) {
            Log.Verbose("No client found. Ignoring position update.");
            return;
        }
        
        var state = CarStates[e.Client.Guid];

        if (!state.MonitorGrip) {
            return;
        }

        if (_sessionManager.ServerTimeMilliseconds - state.LastCollisionTime > config.MonitorTime) {
            state.MonitorGrip = false;
            CarStates[e.Client.Guid] = state;
            return;
        }

        if (state.MonitorGrip) {
            if (state.FirstUpdate == null) {
                state.FirstUpdate = positionUpdate;
            }
            else if (state.FirstUpdate.HasValue) {
                if (IsCarOutOfControl(positionUpdate, state.FirstUpdate.Value)) {
                    // Send no collision event
                    state.MonitorGrip = false;
                    CarStates[e.Client.Guid] = state;
                    _entryCarManager.BroadcastPacket(new ResetCarPacket
                    {
                        Target = e.Client.SessionId
                    });
                    _trafficAi?.GetAiCarBySessionId(e.SessionId).TryResetPosition();
                    Log.Verbose("Reset {client} due to spinning", e.Client.Name);
                }
            }
        }

        CarStates[e.Client.Guid] = state;
    }

    private bool IsCarOutOfControl(PositionUpdateIn current, PositionUpdateIn previous)
    {
        Vector3 movementDirection = Vector3.Subtract(previous.Rotation, current.Rotation);

        // Check if spinning
        Log.Verbose("Movement Direction: {movementDirection}",  MathF.Abs(movementDirection.X));
        bool inSpin = MathF.Abs(movementDirection.X) > config.SpinThreshold;

        // Check if flipping
        Log.Verbose("Movement Direction: {movementDirection}",  MathF.Abs(movementDirection.Y));
        bool inFlip = MathF.Abs(movementDirection.Y) > config.FlipThreshold;

        bool outOfControl = inSpin || inFlip;
        Log.Verbose("Out of Control Check: {movementDirection}, {inSpin}, {inFlip}", movementDirection, inSpin, inFlip);

        return outOfControl;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.CompletedTask;
    }
}

struct CarState {
    public long LastCollisionTime { get; set; }
    public bool MonitorGrip { get; set; }
    public PositionUpdateIn? FirstUpdate { get; set; }
}