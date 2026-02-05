using JetBrains.Annotations;
using YamlDotNet.Serialization;

namespace TimeTrialPlugin.Configuration;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
public class TrackDefinition
{
    [YamlMember(Description = "Unique identifier for this track")]
    public string Id { get; set; } = "";

    [YamlMember(Description = "Display name for this track")]
    public string Name { get; set; } = "";

    [YamlMember(Description = "List of checkpoints for this track")]
    public List<CheckpointDefinition> Checkpoints { get; set; } = [];

    [YamlIgnore]
    public CheckpointDefinition? StartFinish => Checkpoints.FirstOrDefault(c => c.Type == CheckpointType.StartFinish);

    [YamlIgnore]
    public int SectorCount => Checkpoints.Count(c => c.Type == CheckpointType.Sector);

    [YamlIgnore]
    public int TotalCheckpoints => Checkpoints.Count;
}
