using AssettoServer.Server.Plugin;
using Autofac;
using Microsoft.Extensions.Hosting;
using TimeTrialPlugin.Configuration;
using TimeTrialPlugin.Detection;
using TimeTrialPlugin.Timing;

namespace TimeTrialPlugin;

public class TimeTrialModule : AssettoServerModule<TimeTrialConfiguration>
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<TimeTrialPlugin>().AsSelf().AutoActivate().SingleInstance();
        builder.RegisterType<EntryCarTimeTrial>().AsSelf();
        builder.RegisterType<CheckpointDetector>().AsSelf().SingleInstance();
        builder.RegisterType<LeaderboardManager>().AsSelf().SingleInstance();
    }

    public override TimeTrialConfiguration ReferenceConfiguration => new()
    {
        PersistBestTimes = true,
        BestTimesFilePath = "timetrial_bests.json",
        MinStartSpeedKph = 5.0f,
        MaxSectorTimeSeconds = 300.0f,
        ShowLeaderboard = true,
        LeaderboardSize = 10,
        TeleportThresholdMeters = 50.0f,
        InvalidateOnCollision = true,
        InvalidateOnReset = true,
        Tracks =
        [
            new TrackDefinition
            {
                Id = "main_loop",
                Name = "Main Loop",
                Checkpoints =
                [
                    new CheckpointDefinition
                    {
                        Index = 0,
                        Type = CheckpointType.StartFinish,
                        Position = [1234.5f, 10.0f, 5678.9f],
                        Radius = 15.0f
                    },
                    new CheckpointDefinition
                    {
                        Index = 1,
                        Type = CheckpointType.Sector,
                        Position = [2000.0f, 12.0f, 6000.0f],
                        Radius = 12.0f
                    },
                    new CheckpointDefinition
                    {
                        Index = 2,
                        Type = CheckpointType.Sector,
                        Position = [2500.0f, 8.0f, 5500.0f],
                        Radius = 12.0f
                    }
                ]
            }
        ]
    };
}
