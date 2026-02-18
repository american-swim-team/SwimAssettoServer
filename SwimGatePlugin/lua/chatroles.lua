local chatRoleColorEvent = ac.OnlineEvent(
    {
        ac.StructItem.key("swimChatRoleColor"),
        SessionId = ac.StructItem.int32(),
        Color = ac.StructItem.rgbm()
    }, function(sender, message)
        ac.setDriverChatNameColor(message.SessionId, message.Color)
    end)
