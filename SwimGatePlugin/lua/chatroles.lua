local roleColors = {}

local chatRoleColorEvent = ac.OnlineEvent(
    {
        ac.StructItem.key("swimChatRoleColor"),
        CarIndex = ac.StructItem.int32(),
        Color = ac.StructItem.rgbm()
    }, function(sender, message)
        if sender ~= nil then return end
        roleColors[message.CarIndex] = message.Color
        ac.setDriverChatNameColor(message.CarIndex, message.Color)
    end)

local reapplyTimer = 0
function script.update(dt)
    reapplyTimer = reapplyTimer + dt
    if reapplyTimer >= 2 then
        reapplyTimer = 0
        for carIndex, color in pairs(roleColors) do
            ac.setDriverChatNameColor(carIndex, color)
        end
    end
end
