using System.Numerics;
using System.Text.Json;
using AssettoServer.Network.Tcp;
using AssettoServer.Server;
using AssettoServer.Shared.Network.Packets.Incoming;
using Serilog;
using TimeTrialPlugin.Configuration;
using TimeTrialPlugin.Detection;
using TimeTrialPlugin.Packets;
using TimeTrialPlugin.Timing;

namespace TimeTrialPlugin;

public class LapCompletedEventArgs : EventArgs
{
    public required string PlayerName { get; init; }
    public required string TrackName { get; init; }
    public required string FormattedTime { get; init; }
    public required bool IsPersonalBest { get; init; }
}

public class EntryCarTimeTrial
{
    public event EventHandler<LapCompletedEventArgs>? LapCompleted;

    private readonly EntryCar _entryCar;
    private readonly TimeTrialConfiguration _configuration;
    private readonly CheckpointDetector _checkpointDetector;
    private readonly LeaderboardManager _leaderboardManager;
    private readonly SessionManager _sessionManager;

    // Current lap state
    private TrackDefinition? _currentTrack;
    private int _nextCheckpointIndex;
    private long _lapStartTime;
    private long _lastCheckpointTime;
    private long[] _sectorTimes = [];
    private bool _lapValid = true;
    private string _invalidationReason = "";

    // Position tracking for teleport detection
    private Vector3 _lastPosition;
    private bool _hasLastPosition;

    public EntryCarTimeTrial(
        EntryCar entryCar,
        TimeTrialConfiguration configuration,
        CheckpointDetector checkpointDetector,
        LeaderboardManager leaderboardManager,
        SessionManager sessionManager)
    {
        _entryCar = entryCar;
        _configuration = configuration;
        _checkpointDetector = checkpointDetector;
        _leaderboardManager = leaderboardManager;
        _sessionManager = sessionManager;

        _entryCar.PositionUpdateReceived += OnPositionUpdateReceived;
        _entryCar.ResetInvoked += OnResetInvoked;
    }

    public void Dispose()
    {
        _entryCar.PositionUpdateReceived -= OnPositionUpdateReceived;
        _entryCar.ResetInvoked -= OnResetInvoked;
    }

    public void OnClientConnected()
    {
        _hasLastPosition = false;
        _currentTrack = null;
    }

    public void OnCollision()
    {
        Log.Debug("EntryCarTimeTrial.OnCollision: InvalidateOnCollision={Enabled}, CurrentTrack={Track}, LapValid={Valid}",
            _configuration.InvalidateOnCollision, _currentTrack?.Name ?? "null", _lapValid);

        if (_configuration.InvalidateOnCollision && _currentTrack != null && _lapValid)
        {
            InvalidateLap("Collision detected");
        }
    }

    private void OnResetInvoked(EntryCar sender, EventArgs args)
    {
        if (_configuration.InvalidateOnReset && _currentTrack != null && _lapValid)
        {
            InvalidateLap("Car reset");
        }
        _currentTrack = null;
        _hasLastPosition = false;
    }

    private void OnPositionUpdateReceived(EntryCar sender, in PositionUpdateIn positionUpdate)
    {
        var currentPosition = positionUpdate.Position;
        var currentTime = _sessionManager.ServerTimeMilliseconds;

        // Teleport detection
        if (_hasLastPosition && _currentTrack != null && _lapValid)
        {
            var distanceSquared = Vector3.DistanceSquared(_lastPosition, currentPosition);
            if (distanceSquared > _configuration.TeleportThresholdSquared)
            {
                InvalidateLap("Teleport detected");
            }
        }

        // Process checkpoint detection
        if (_hasLastPosition)
        {
            ProcessPosition(_lastPosition, currentPosition, currentTime);
        }

        _lastPosition = currentPosition;
        _hasLastPosition = true;
    }

    private void ProcessPosition(Vector3 previousPosition, Vector3 currentPosition, long currentTime)
    {
        // If no lap in progress, check for start crossing on any track
        if (_currentTrack == null)
        {
            var (track, checkpoint) = _checkpointDetector.DetectStartCrossing(previousPosition, currentPosition);
            if (track != null && checkpoint != null)
            {
                StartLap(track, currentTime);
            }
            return;
        }

        // Sector timeout check
        if (_lapValid && currentTime - _lastCheckpointTime > _configuration.MaxSectorTimeMilliseconds)
        {
            InvalidateLap("Sector timeout");
            return;
        }

        // Check for checkpoint crossing
        var crossedCheckpoint = _checkpointDetector.DetectCheckpointCrossing(
            previousPosition,
            currentPosition,
            _currentTrack,
            _nextCheckpointIndex);

        if (crossedCheckpoint == null) return;

        if (crossedCheckpoint.Type == CheckpointType.StartFinish || crossedCheckpoint.Type == CheckpointType.Finish)
        {
            // Completed lap or starting new lap (for circular tracks)
            if (crossedCheckpoint.Type == CheckpointType.StartFinish && _nextCheckpointIndex == 0)
            {
                // Starting a new lap (shouldn't happen here, but handle it)
                StartLap(_currentTrack, currentTime);
            }
            else
            {
                // Completed a lap
                CompleteLap(currentTime);
            }
        }
        else
        {
            // Sector checkpoint
            HandleSectorCheckpoint(crossedCheckpoint, currentTime);
        }
    }

    private void StartLap(TrackDefinition track, long currentTime)
    {
        // Check minimum speed
        var speed = _entryCar.Status.Velocity.Length();
        if (speed < _configuration.MinStartSpeedMs)
        {
            Log.Debug("StartLap rejected: speed {Speed} < min {MinSpeed}", speed, _configuration.MinStartSpeedMs);
            return;
        }

        _currentTrack = track;
        _lapStartTime = currentTime;
        _lastCheckpointTime = currentTime;
        _nextCheckpointIndex = 1; // Next checkpoint after start/finish
        _sectorTimes = new long[track.TotalCheckpoints - 1]; // Exclude start/finish
        _lapValid = true;
        _invalidationReason = "";

        var pb = GetPersonalBest(track.Id);

        _entryCar.Client?.SendPacket(new LapStartPacket
        {
            TrackId = track.Id,
            TrackName = track.Name,
            TotalCheckpoints = track.TotalCheckpoints,
            PersonalBestMs = (int)(pb?.TotalTimeMs ?? 0)
        });

        Log.Information("Player {Name} started lap on {Track} (checkpoints: {Count})",
            _entryCar.Client?.Name, track.Name, track.TotalCheckpoints);
    }

    private void HandleSectorCheckpoint(CheckpointDefinition checkpoint, long currentTime)
    {
        if (_currentTrack == null) return;

        var sectorTime = currentTime - _lastCheckpointTime;
        var totalTime = currentTime - _lapStartTime;

        // Store sector time (0-indexed array, checkpoint index starts at 1 for sectors)
        var sectorArrayIndex = checkpoint.Index - 1;
        if (sectorArrayIndex >= 0 && sectorArrayIndex < _sectorTimes.Length)
        {
            _sectorTimes[sectorArrayIndex] = sectorTime;
        }

        _lastCheckpointTime = currentTime;
        _nextCheckpointIndex = checkpoint.Index + 1;

        // If next checkpoint would be past the last, wrap to start/finish (index 0)
        if (_nextCheckpointIndex >= _currentTrack.TotalCheckpoints)
        {
            _nextCheckpointIndex = 0;
        }

        // Calculate delta to PB sector time
        var pb = GetPersonalBest(_currentTrack.Id);
        var pbSectorTime = pb != null && sectorArrayIndex < pb.SectorTimesMs.Length
            ? pb.SectorTimesMs[sectorArrayIndex]
            : 0;
        var deltaToPb = pbSectorTime > 0 ? (int)(sectorTime - pbSectorTime) : 0;

        _entryCar.Client?.SendPacket(new SectorCrossedPacket
        {
            TrackId = _currentTrack.Id,
            SectorIndex = checkpoint.Index,
            SectorTimeMs = (int)sectorTime,
            TotalTimeMs = (int)totalTime,
            DeltaToPbMs = deltaToPb,
            IsValid = _lapValid
        });

        Log.Debug("Player {Name} crossed sector {Sector} on {Track}: {Time}ms (delta: {Delta}ms)",
            _entryCar.Client?.Name, checkpoint.Index, _currentTrack.Name, sectorTime, deltaToPb);
    }

    private void CompleteLap(long currentTime)
    {
        Log.Debug("CompleteLap called: CurrentTrack={Track}, LapValid={Valid}, Client={Client}",
            _currentTrack?.Name ?? "null", _lapValid, _entryCar.Client?.Name ?? "null");

        if (_currentTrack == null) return;

        // Record final sector time
        var finalSectorTime = currentTime - _lastCheckpointTime;
        var finalSectorIndex = _sectorTimes.Length - 1;
        if (finalSectorIndex >= 0)
        {
            _sectorTimes[finalSectorIndex] = finalSectorTime;
        }

        var totalTime = currentTime - _lapStartTime;

        if (!_lapValid)
        {
            Log.Information("Player {Name} completed invalid lap on {Track}: {Time}ms (reason: {Reason})",
                _entryCar.Client?.Name, _currentTrack.Name, totalTime, _invalidationReason);

            // Reset for next lap
            _currentTrack = null;
            return;
        }

        var client = _entryCar.Client;
        if (client == null)
        {
            Log.Warning("CompleteLap: client is null, cannot record lap");
            _currentTrack = null;
            return;
        }

        // Get personal best before recording
        var previousPb = GetPersonalBest(_currentTrack.Id);
        var deltaToPb = previousPb != null ? (int)(totalTime - previousPb.TotalTimeMs) : 0;

        // Record the lap time
        var lapTime = new LapTime
        {
            TrackId = _currentTrack.Id,
            PlayerName = client.Name ?? "Unknown",
            PlayerGuid = client.Guid,
            CarModel = _entryCar.Model,
            TotalTimeMs = totalTime,
            SectorTimesMs = _sectorTimes.ToArray(),
            RecordedAt = DateTime.UtcNow
        };

        var isPersonalBest = _leaderboardManager.RecordLapTime(lapTime);

        _entryCar.Client?.SendPacket(new LapCompletedPacket
        {
            TrackId = _currentTrack.Id,
            TotalTimeMs = (int)totalTime,
            IsPersonalBest = isPersonalBest,
            DeltaToPbMs = deltaToPb,
            SectorTimesJson = JsonSerializer.Serialize(_sectorTimes)
        });

        Log.Information("Player {Name} completed lap on {Track}: {Time}ms (PB: {IsPb})",
            client.Name, _currentTrack.Name, totalTime, isPersonalBest);

        // Raise event for chat broadcast
        LapCompleted?.Invoke(this, new LapCompletedEventArgs
        {
            PlayerName = client.Name ?? "Unknown",
            TrackName = _currentTrack.Name,
            FormattedTime = lapTime.FormattedTotalTime,
            IsPersonalBest = isPersonalBest
        });

        // Reset for next lap
        _currentTrack = null;
    }

    private void InvalidateLap(string reason)
    {
        if (!_lapValid || _currentTrack == null) return;

        _lapValid = false;
        _invalidationReason = reason;

        _entryCar.Client?.SendPacket(new InvalidationPacket
        {
            TrackId = _currentTrack.Id,
            Reason = reason
        });

        Log.Debug("Player {Name} lap invalidated on {Track}: {Reason}",
            _entryCar.Client?.Name, _currentTrack.Name, reason);

        // Reset state so player can start a new lap
        _currentTrack = null;
        _nextCheckpointIndex = 0;
    }

    private LapTime? GetPersonalBest(string trackId)
    {
        var client = _entryCar.Client;
        if (client == null) return null;
        return _leaderboardManager.GetPersonalBest(trackId, client.Guid);
    }

    public void SendTrackInfo()
    {
        var client = _entryCar.Client;
        if (client == null) return;

        var trackInfos = _configuration.Tracks.Select(t => new
        {
            t.Id,
            t.Name,
            t.TotalCheckpoints,
            SectorCount = t.SectorCount
        });

        var personalBests = _configuration.Tracks
            .Select(t => new
            {
                TrackId = t.Id,
                Time = _leaderboardManager.GetPersonalBest(t.Id, client.Guid)?.TotalTimeMs ?? 0
            })
            .Where(pb => pb.Time > 0)
            .ToList();

        client.SendPacket(new TrackInfoPacket
        {
            TracksJson = JsonSerializer.Serialize(trackInfos),
            PersonalBestsJson = JsonSerializer.Serialize(personalBests)
        });
    }

}
