using AssettoServer.Network.ClientMessages;

namespace SwimCutupPlugin.Packets;

[OnlineEvent(Key = "SwimCutupMsg")]
public class SwimCutupMsg : OnlineEvent<SwimCutupMsg>
{
    [OnlineEventField(Name = "SwimCutupMsg")]
    public MsgType MsgType;
    public long Payload;
}

public enum MsgType : byte
{
    Initialize = 1,
    NewHighscore = 2
}
