

-- local function window_SwimCrash()
-- end

-- ui.registerOnlineExtra(ui.Icons.Driver, "swim> crash", nil, window_SwimCrash(), nil, ui.OnlineExtraFlags.Tool)

local collisionUpdateEvent = ac.OnlineEvent({
    ac.StructItem.key("AS_CollisionUpdate"),
    enabled = ac.StructItem.boolean(),
    target = ac.StructItem.byte()
}, function (sender, message)
    ac.debug("collision_update_index", sender.index)
    ac.debug("collision_update_enabled", message.enabled)

    physics.disableCarCollisions(0, not message.enabled)
    if sender.index == 0 then
        for i, c in ac.iterateCars.ordered() do
            physics.disableCarCollisions(i, not message.enabled)
        end
    else
        physics.disableCarCollisions(sender.index, not message.enabled)
    end
end)

function script.update(dt)
