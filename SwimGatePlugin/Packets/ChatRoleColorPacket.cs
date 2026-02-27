using System.Drawing;
using AssettoServer.Network.ClientMessages;

namespace SwimGatePlugin.Packets;

[OnlineEvent(Key = "swimChatRoleColor")]
public class ChatRoleColorPacket : OnlineEvent<ChatRoleColorPacket>
{
    [OnlineEventField(Name = "sessionId")]
    public int TargetSessionId;

    [OnlineEventField(Name = "Color")]
    public Color Color;
}
