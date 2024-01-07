using AssettoServer.Network.ClientMessages;

namespace SwimCutupPlugin.Packets;

[OnlineEvent(Key = "SwimCutupMsg")]
public class SwimCutupMsg : OnlineEvent<SwimCutupMsg>
{
    [OnlineEventField(Name = "MsgType")]
    public long MsgType;
    [OnlineEventField(Name = "Payload")]
    public long Payload;
}