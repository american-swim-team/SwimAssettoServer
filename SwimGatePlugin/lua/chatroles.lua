local roleColors = {}

local function applyColorBySession(sessionId, color)
    local car = ac.getCar.serverSlot(sessionId)
    if car then
        ac.setDriverChatNameColor(car.index, color)
    end
end

local chatRoleColorEvent = ac.OnlineEvent(
    {
        ac.StructItem.key("swimChatRoleColor"),
        sessionId = ac.StructItem.int32(),
        Color = ac.StructItem.rgbm()
    }, function(sender, message)
        if sender ~= nil then return end
        roleColors[message.sessionId] = message.Color
        applyColorBySession(message.sessionId, message.Color)
    end)

local reapplyTimer = 0
function script.update(dt)
    reapplyTimer = reapplyTimer + dt
    if reapplyTimer >= 2 then
        reapplyTimer = 0
        for sessionId, color in pairs(roleColors) do
            applyColorBySession(sessionId, color)
        end
    end
end
