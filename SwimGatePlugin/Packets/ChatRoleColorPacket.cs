using System.Drawing;
using AssettoServer.Network.ClientMessages;

namespace SwimGatePlugin.Packets;

[OnlineEvent(Key = "swimChatRoleColor")]
public class ChatRoleColorPacket : OnlineEvent<ChatRoleColorPacket>
{
    [OnlineEventField(Name = "SessionId")]
    public new int SessionId;

    [OnlineEventField(Name = "Color")]
    public Color Color;
}
