using System.Numerics;
using JetBrains.Annotations;
using YamlDotNet.Serialization;

namespace TimeTrialPlugin.Configuration;

public enum CheckpointType
{
    StartFinish,
    Start,
    Finish,
    Sector
}

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
public class CheckpointDefinition
{
    [YamlMember(Description = "Checkpoint index (0 = start/finish, 1+ = sectors)")]
    public int Index { get; set; }

    [YamlMember(Description = "Type of checkpoint: StartFinish or Sector")]
    public CheckpointType Type { get; set; } = CheckpointType.Sector;

    [YamlMember(Description = "Position [X, Y, Z] of the checkpoint center")]
    public List<float> Position { get; set; } = [0, 0, 0];

    [YamlMember(Description = "Radius of the checkpoint detection sphere in meters")]
    public float Radius { get; set; } = 12.0f;

    [YamlMember(Description = "Expected travel direction [X, Y, Z] for start checkpoints (optional). When set, only players moving in this direction will trigger a run start.")]
    public List<float>? Direction { get; set; }

    [YamlMember(Description = "Maximum angle in degrees between player velocity and expected direction (default 90 = 180° forward cone)")]
    public float DirectionToleranceDegrees { get; set; } = 90;

    [YamlIgnore]
    public Vector3 PositionVector => new(Position[0], Position[1], Position[2]);

    [YamlIgnore]
    public Vector3? DirectionVector => Direction != null ? new Vector3(Direction[0], Direction[1], Direction[2]) : null;

    [YamlIgnore]
    public float RadiusSquared => Radius * Radius;
}
