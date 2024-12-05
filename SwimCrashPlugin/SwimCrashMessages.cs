using AssettoServer.Network.ClientMessages;

namespace SwimCrashPlugin;

[OnlineEvent(Key = "SW_ResetCar")]
public class ResetCarPacket : OnlineEvent<ResetCarPacket>
{
    [OnlineEventField(Name = "target")]
    public byte Target;
}