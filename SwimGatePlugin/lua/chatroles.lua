local chatRoleColorEvent = ac.OnlineEvent(
    {
        ac.StructItem.key("swimChatRoleColor"),
        CarIndex = ac.StructItem.int32(),
        Color = ac.StructItem.rgbm()
    }, function(sender, message)
        if sender ~= nil then return end
        ac.setDriverChatNameColor(message.CarIndex, message.Color)
    end)
