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

local sim = ac.getSim()
local uiState = ac.getUI()
local resetEvents = 0
local iconDisplayTimer = nil

-- get players server slot
local slot = ac.getCar(0).sessionID
ac.debug("Server slot:", slot)

local resetEvent = ac.OnlineEvent({
    ac.StructItem.key("SW_ResetCar"),
    target = ac.StructItem.byte(),
}, function(sender, message)
    resetEvents = resetEvents + 1
    ac.debug("Resets", resetEvents)
    if slot == message.target then
        iconDisplayTimer = 0
    end
end)

function script.update(dt)
    if iconDisplayTimer then
        iconDisplayTimer = iconDisplayTimer + dt
        if iconDisplayTimer > 10 then
            iconDisplayTimer = nil
        end
    end
end

function script.drawUI()
    ui.transparentWindow("swimCrash", vec2(uiState.windowSize.x / 2 - 83, 80), vec2(166, 159), function()
        if iconDisplayTimer then
            ui.drawImage("http://static.swimserver.com/car-crash-icon.png", vec2(0, 0), vec2(166, 159))
        end
    end)
end
