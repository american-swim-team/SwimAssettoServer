-- Coded by Romedius
--[[
                                    __
                   __              /\ `\
  ____  __  __  __/\_\    ___ ___  \ `\ `\
 /',__\/\ \/\ \/\ \/\ \ /' __` __`\ `\ >  >
/\__, `\ \ \_/ \_/ \ \ \/\ \/\ \/\ \  /  /
\/\____/\ \___x___/'\ \_\ \_\ \_\ \_\/\_/
 \/___/  \/__//__/   \/_/\/_/\/_/\/_/\//


Custom License

This software is licensed to wheres981 and romedius.
This software is the intellectual property of the aforementioned individuals.

The licensed individuals are free to distribute, modify, and use the software internally in any organization they are
both members of.
These two individuals may use and modify the code, only distributing the code at each other's discretion.

Outside organizations (those the licensed individuals are not a part of) may not distribute or modify the code and can
only use the code as a user/client of the licensed individuals.

Outside individuals (those not expressly given permission by the licensed individuals to modify or distribute the code)
may not distribute or modify the code and can only use the code as a user/client of the licensed individuals.

An individual or organization is a user/client if they are using this code as a provided service, free or otherwise,
by the licensed individuals.

Changes to this license can be made by the licensed individuals at any time, so long as they both agree to the changes.

This software is provided "as is", without warranty of any kind, express or implied, including but not limited to the
warranties of merchantability, fitness for a particular purpose, and noninfringement. In no event shall the authors or
copyright holders be liable for any claim, damages, or other liability, whether in an action of contract, tort, or
otherwise, arising from, out of, or in connection with the software or the use or other dealings in the software.

Any modifications to the code made by the licensed individuals must be clearly documented and any modifications made by
outside organizations or individuals must be approved in writing by the licensed individuals before they can be
distributed or used.

]]
--

local uiState = ac.getUI()
local highlightColor = rgb(1.0, 0.5, 0.0)

-- get players server slot
local slot = ac.getCar(0).sessionID
ac.debug("Server slot:", slot)

-- track which session IDs have collisions disabled
local collisionDisabledCars = {}

ac.OnlineEvent({
    ac.StructItem.key("SW_CollisionState"),
    target = ac.StructItem.byte(),
    enabled = ac.StructItem.byte(),
}, function(sender, message)
    if message.enabled == 0 then
        collisionDisabledCars[message.target] = true
    else
        collisionDisabledCars[message.target] = nil
    end
    ac.debug("Collision disabled cars", table.nkeys(collisionDisabledCars))
end)

function script.update(dt)
    local sim = ac.getSim()
    for i = 0, sim.carsCount - 1 do
        local car = ac.getCar(i)
        if i ~= 0 and collisionDisabledCars[car.sessionID] then
            ac.highlightCar(i, highlightColor)
        end
    end
end

function script.drawUI()
    ui.transparentWindow("swimCrash", vec2(uiState.windowSize.x / 2 - 83, 80), vec2(166, 159), function()
        if collisionDisabledCars[slot] then
            ui.drawImage("http://static.swimserver.com/car-crash-icon.png", vec2(0, 0), vec2(166, 159))
        end
    end)
end
