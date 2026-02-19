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
    /// Find the start/finish or start checkpoint for any track at the given position.
    /// Used to automatically select which track the player is starting on.
    /// When a checkpoint has a Direction configured, the player's velocity must
    /// align within the tolerance angle to trigger a start.
    /// </summary>
    public (TrackDefinition? Track, CheckpointDefinition? Checkpoint) DetectStartCrossing(
        Vector3 previousPosition,
        Vector3 currentPosition,
        Vector3 velocity)
    {
        foreach (var track in _configuration.Tracks)
        {
            var startCheckpoint = track.StartCheckpoint;
            if (startCheckpoint == null) continue;

            var wasInside = IsInCheckpoint(previousPosition, startCheckpoint);
            var isInside = IsInCheckpoint(currentPosition, startCheckpoint);

            if (!wasInside && isInside)
            {
                if (!IsDirectionAligned(velocity, startCheckpoint))
                    continue;

                return (track, startCheckpoint);
            }
        }
        return (null, null);
    }

    /// <summary>
    /// Check if velocity aligns with the checkpoint's expected direction.
    /// Uses only the XZ (horizontal) plane to ignore vertical movement.
    /// Returns true if no direction is configured (backwards compatible).
    /// </summary>
    private static bool IsDirectionAligned(Vector3 velocity, CheckpointDefinition checkpoint)
    {
        var direction = checkpoint.DirectionVector;
        if (direction == null) return true;

        // Project both vectors onto XZ plane
        var velXZ = new Vector2(velocity.X, velocity.Z);
        var dirXZ = new Vector2(direction.Value.X, direction.Value.Z);

        var velLength = velXZ.Length();
        var dirLength = dirXZ.Length();

        // If either vector has near-zero XZ magnitude, skip direction check
        if (velLength < 0.001f || dirLength < 0.001f) return true;

        var dot = Vector2.Dot(velXZ / velLength, dirXZ / dirLength);
        var cosThreshold = MathF.Cos(checkpoint.DirectionToleranceDegrees * MathF.PI / 180f);

        return dot >= cosThreshold;
    }
}
