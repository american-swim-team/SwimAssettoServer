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

    [YamlIgnore]
    public Vector3 PositionVector => new(Position[0], Position[1], Position[2]);

    [YamlIgnore]
    public float RadiusSquared => Radius * Radius;
}
