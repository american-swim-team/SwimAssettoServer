using System.Numerics;
using TimeTrialPlugin.Configuration;

namespace TimeTrialPlugin.Detection;

public class CheckpointDetector
{
    private readonly TimeTrialConfiguration _configuration;

    public CheckpointDetector(TimeTrialConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Check if a position is within a checkpoint's detection radius.
    /// </summary>
    public bool IsInCheckpoint(Vector3 position, CheckpointDefinition checkpoint)
    {
        var distanceSquared = Vector3.DistanceSquared(position, checkpoint.PositionVector);
        return distanceSquared <= checkpoint.RadiusSquared;
    }

    /// <summary>
    /// Find which checkpoint (if any) the given position is in.
    /// Returns null if not in any checkpoint.
    /// </summary>
    public CheckpointDefinition? GetCheckpointAtPosition(Vector3 position, TrackDefinition track)
    {
        foreach (var checkpoint in track.Checkpoints)
        {
            if (IsInCheckpoint(position, checkpoint))
            {
                return checkpoint;
            }
        }
        return null;
    }

    /// <summary>
    /// Check if player crossed from outside to inside a checkpoint between two positions.
    /// Returns the crossed checkpoint if a crossing occurred.
    /// </summary>
    public CheckpointDefinition? DetectCheckpointCrossing(
        Vector3 previousPosition,
        Vector3 currentPosition,
        TrackDefinition track,
        int expectedCheckpointIndex)
    {
        foreach (var checkpoint in track.Checkpoints)
        {
            if (checkpoint.Index != expectedCheckpointIndex) continue;

            var wasInside = IsInCheckpoint(previousPosition, checkpoint);
            var isInside = IsInCheckpoint(currentPosition, checkpoint);

            // Crossing occurs when entering the checkpoint
            if (!wasInside && isInside)
            {
                return checkpoint;
            }
        }
        return null;
    }

    /// <summary>
    /// Find the start/finish checkpoint for any track at the given position.
    /// Used to automatically select which track the player is starting on.
    /// </summary>
    public (TrackDefinition? Track, CheckpointDefinition? Checkpoint) DetectStartFinishCrossing(
        Vector3 previousPosition,
        Vector3 currentPosition)
    {
        foreach (var track in _configuration.Tracks)
        {
            var startFinish = track.StartFinish;
            if (startFinish == null) continue;

            var wasInside = IsInCheckpoint(previousPosition, startFinish);
            var isInside = IsInCheckpoint(currentPosition, startFinish);

            if (!wasInside && isInside)
            {
                return (track, startFinish);
            }
        }
        return (null, null);
    }
}
