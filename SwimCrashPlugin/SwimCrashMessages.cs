using AssettoServer.Network.ClientMessages;

namespace SwimCrashPlugin;

[OnlineEvent(Key = "SW_ResetCar")]
public class ResetCarPacket : OnlineEvent<ResetCarPacket>
{
    [OnlineEventField(Name = "target")]
    public byte Target;
}

[OnlineEvent(Key = "SW_CollisionState")]
public class CollisionStatePacket : OnlineEvent<CollisionStatePacket>
{
    [OnlineEventField(Name = "target")]
    public byte Target;

    [OnlineEventField(Name = "enabled")]
    public byte Enabled;  // 1 = collisions on, 0 = collisions off
}