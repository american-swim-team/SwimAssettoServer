using AssettoServer.Server.Configuration;
using JetBrains.Annotations;
using YamlDotNet.Serialization;

namespace TimeTrialPlugin.Configuration;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
public class TimeTrialConfiguration : IValidateConfiguration<TimeTrialConfigurationValidator>
{
    [YamlMember(Description = "Persist best times to a file")]
    public bool PersistBestTimes { get; set; } = true;

    [YamlMember(Description = "File path for persisted best times (relative to server directory)")]
    public string BestTimesFilePath { get; set; } = "timetrial_bests.json";

    [YamlMember(Description = "Minimum speed in km/h required to start a lap (prevents false starts)")]
    public float MinStartSpeedKph { get; set; } = 5.0f;

    [YamlMember(Description = "Maximum time in seconds allowed per sector before lap is invalidated")]
    public float MaxSectorTimeSeconds { get; set; } = 300.0f;

    [YamlMember(Description = "Show leaderboard in the UI")]
    public bool ShowLeaderboard { get; set; } = true;

    [YamlMember(Description = "Number of entries to show in leaderboard")]
    public int LeaderboardSize { get; set; } = 10;

    [YamlMember(Description = "Distance threshold in meters for teleport detection (invalidates lap)")]
    public float TeleportThresholdMeters { get; set; } = 50.0f;

    [YamlMember(Description = "Enable collision detection for lap invalidation")]
    public bool InvalidateOnCollision { get; set; } = true;

    [YamlMember(Description = "Enable car reset detection for lap invalidation")]
    public bool InvalidateOnReset { get; set; } = true;

    [YamlMember(Description = "List of time trial tracks")]
    public List<TrackDefinition> Tracks { get; set; } = [];

    [YamlIgnore]
    public float MinStartSpeedMs => MinStartSpeedKph / 3.6f;

    [YamlIgnore]
    public int MaxSectorTimeMilliseconds => (int)(MaxSectorTimeSeconds * 1000);

    [YamlIgnore]
    public float TeleportThresholdSquared => TeleportThresholdMeters * TeleportThresholdMeters;
}
