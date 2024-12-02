using AssettoServer.Server.Plugin;
using AssettoServer.Server;
using AssettoServer.Shared.Network.Packets.Incoming;
using AssettoServer.Network.ClientMessages;
using System.Numerics;
using AssettoServer.Network.Tcp;
using Serilog;
using Microsoft.Extensions.Hosting;

namespace SwimCrashPlugin;

[OnlineEvent(Key = "noCollision")]
public class noCollision : OnlineEvent<noCollision>
{
    [OnlineEventField(Name = "entryCar")]
    public int EntryCar;
}

public class SwimCrashHandler : BackgroundService, IAssettoServerAutostart
{
    private readonly SessionManager _sessionManager;
    private readonly EntryCarManager _entryCarManager;
    private Dictionary<ulong, CarState> CarStates = new Dictionary<ulong, CarState>();
    public SwimCrashHandler(SessionManager sessionManager, EntryCarManager entryCarManager)
    {
        _sessionManager = sessionManager;
        _entryCarManager = entryCarManager;
        _entryCarManager.ClientConnected += OnConnected;
        _entryCarManager.ClientDisconnected += OnDisconnected;
    }

    private void OnConnected(ACTcpClient sender, EventArgs e)
    {
        Log.Debug("Client registered for collision management");
        sender.Collision += OnCollision;
        sender.EntryCar.PositionUpdateReceived += OnPositionUpdateReceived;
        CarStates[sender.Guid] = new CarState();
    }

    private void OnDisconnected(ACTcpClient sender, EventArgs e)
    {
        Log.Debug("Client unregistered for collision management");
        sender.Collision -= OnCollision;
        sender.EntryCar.PositionUpdateReceived -= OnPositionUpdateReceived;
        CarStates.Remove(sender.Guid);
    }

    private void OnCollision(ACTcpClient sender, CollisionEventArgs e) {
        Log.Debug("Collision detected");
        if (e.Speed < 30) {
            return;
        }

        var state = CarStates[sender.Guid];

        state.MonitorGrip = true;
        state.LastCollisionTime = _sessionManager.ServerTimeMilliseconds;
        CarStates[sender.Guid] = state;
    }

    private void OnPositionUpdateReceived(EntryCar e, in PositionUpdateIn positionUpdate) {
        if (e.Client == null) {
            Log.Debug("No client found. Ignoring position update.");
            return;
        }
        
        var state = CarStates[e.Client.Guid];

        if (!state.MonitorGrip) {
            return;
        }

        if (_sessionManager.ServerTimeMilliseconds - state.LastCollisionTime > 3000) {
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
                    e.TryResetPosition();
                    Log.Debug("Reset {client} due to spinning", e.Client.Name);
                }
            }
        }

        state.FirstUpdate = positionUpdate;
        CarStates[e.Client.Guid] = state;
    }

    private bool IsCarOutOfControl(PositionUpdateIn current, PositionUpdateIn previous)
{
    Vector3 forwardDirection = GetForwardDirection(current.Rotation);

    Vector3 lateralVelocity = current.Velocity - Vector3.Dot(current.Velocity, forwardDirection) * forwardDirection;
    float lateralVelocityMagnitude = lateralVelocity.Length();

    float rotationInstability = Math.Abs(current.Rotation.X);

    int tyreSpeedDifference = Math.Abs(current.TyreAngularSpeedFL - current.TyreAngularSpeedFR) +
                              Math.Abs(current.TyreAngularSpeedRL - current.TyreAngularSpeedRR);

    bool isSpinning = rotationInstability > 1.0f;
    bool isSliding = lateralVelocityMagnitude > 20.0f && current.Gas > 0.5f;
    bool tyreSlipDetected = tyreSpeedDifference > 10;

    bool outOfControl = (isSpinning && isSliding) || tyreSlipDetected;

    Log.Debug("Out of Control Check: {rotationInstability}, {lateralVelocity}, {tyreSlip}, {isSpinning}, {isSliding}, {tyreSlipDetected}",
        rotationInstability, lateralVelocityMagnitude, tyreSpeedDifference, isSpinning, isSliding, tyreSlipDetected);

    return outOfControl;
}


    private static Vector3 GetForwardDirection(Vector3 rotation)
    {
        // Assuming rotation.Y is the yaw angle in radians
        float yaw = rotation.Y;

        return new Vector3(MathF.Cos(yaw), 0.0f, MathF.Sin(yaw));
    }

    private static float Clamp(float value, float min, float max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
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