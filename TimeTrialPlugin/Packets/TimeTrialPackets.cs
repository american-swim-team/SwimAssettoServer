using AssettoServer.Network.ClientMessages;

namespace TimeTrialPlugin.Packets;

[OnlineEvent(Key = "TT_SectorCrossed")]
public class SectorCrossedPacket : OnlineEvent<SectorCrossedPacket>
{
    [OnlineEventField(Name = "trackId", Size = 64)]
    public string TrackId = "";

    [OnlineEventField(Name = "sectorIndex")]
    public int SectorIndex;

    [OnlineEventField(Name = "sectorTimeMs")]
    public int SectorTimeMs;

    [OnlineEventField(Name = "totalTimeMs")]
    public int TotalTimeMs;

    [OnlineEventField(Name = "deltaToPbMs")]
    public int DeltaToPbMs;

    [OnlineEventField(Name = "isValid")]
    public bool IsValid;
}

[OnlineEvent(Key = "TT_LapCompleted")]
public class LapCompletedPacket : OnlineEvent<LapCompletedPacket>
{
    [OnlineEventField(Name = "trackId", Size = 64)]
    public string TrackId = "";

    [OnlineEventField(Name = "totalTimeMs")]
    public int TotalTimeMs;

    [OnlineEventField(Name = "isPersonalBest")]
    public bool IsPersonalBest;

    [OnlineEventField(Name = "leaderboardPosition")]
    public int LeaderboardPosition;

    [OnlineEventField(Name = "deltaToPbMs")]
    public int DeltaToPbMs;

    [OnlineEventField(Name = "sectorTimesJson", Size = 256)]
    public string SectorTimesJson = "";
}

[OnlineEvent(Key = "TT_Invalidated")]
public class InvalidationPacket : OnlineEvent<InvalidationPacket>
{
    [OnlineEventField(Name = "trackId", Size = 64)]
    public string TrackId = "";

    [OnlineEventField(Name = "reason", Size = 64)]
    public string Reason = "";
}

[OnlineEvent(Key = "TT_Leaderboard")]
public class LeaderboardUpdatePacket : OnlineEvent<LeaderboardUpdatePacket>
{
    [OnlineEventField(Name = "trackId", Size = 64)]
    public string TrackId = "";

    [OnlineEventField(Name = "entriesJson", Size = 2048)]
    public string EntriesJson = "";
}

[OnlineEvent(Key = "TT_TrackInfo")]
public class TrackInfoPacket : OnlineEvent<TrackInfoPacket>
{
    [OnlineEventField(Name = "tracksJson", Size = 1024)]
    public string TracksJson = "";

    [OnlineEventField(Name = "personalBestsJson", Size = 1024)]
    public string PersonalBestsJson = "";
}

[OnlineEvent(Key = "TT_LapStart")]
public class LapStartPacket : OnlineEvent<LapStartPacket>
{
    [OnlineEventField(Name = "trackId", Size = 64)]
    public string TrackId = "";

    [OnlineEventField(Name = "trackName", Size = 64)]
    public string TrackName = "";

    [OnlineEventField(Name = "totalCheckpoints")]
    public int TotalCheckpoints;

    [OnlineEventField(Name = "personalBestMs")]
    public int PersonalBestMs;
}
