--[[
Time Trial Plugin - Client UI
Displays lap timing, sector splits, personal bests, and leaderboard
]]

-- Theme (Obsidian Glass)
local Theme = {
    glass = rgbm(0.047, 0.047, 0.059, 0.85),
    glassHighlight = rgbm(1, 1, 1, 0.04),
    textHero = rgbm(1, 1, 1, 1),
    textPrimary = rgbm(0.941, 0.941, 0.949, 1),
    textSecondary = rgbm(0.659, 0.659, 0.690, 1),
    textMuted = rgbm(0.376, 0.376, 0.408, 1),
    accent = rgbm(0.647, 0.769, 0.831, 1),
    success = rgbm(0.639, 0.902, 0.208, 1),
    error = rgbm(0.973, 0.443, 0.443, 1),
}

local UI = {
    width = 240,
    padding = 14,
    cornerRadius = 12,
}

local Settings = {
    hudEnabled = true,
    hudPosition = 2,
}

-- State
local State = {
    tracks = {},
    personalBests = {},
    leaderboards = {},
    activeLap = nil,
    lastLapResult = nil,
    lastInvalidation = nil,
}

-- Helpers
local function formatTime(ms)
    if ms <= 0 then return "--:--.---" end
    local totalSeconds = ms / 1000
    local minutes = math.floor(totalSeconds / 60)
    local seconds = totalSeconds % 60
    if minutes >= 1 then
        return string.format("%d:%05.3f", minutes, seconds)
    else
        return string.format("%.3f", seconds)
    end
end

local function formatDelta(deltaMs)
    if deltaMs == 0 then return "" end
    local sign = deltaMs > 0 and "+" or ""
    return sign .. formatTime(math.abs(deltaMs))
end

local function drawPanel(x, y, w, h, radius, fillColor)
    ui.drawRectFilled(vec2(x, y), vec2(x + w, y + h), fillColor, radius)
end

-- Event Handlers
local lapStartEvent = ac.OnlineEvent({
    ac.StructItem.key("TT_LapStart"),
    trackId = ac.StructItem.string(64),
    trackName = ac.StructItem.string(64),
    totalCheckpoints = ac.StructItem.int32(),
    personalBestMs = ac.StructItem.int32()
}, function(sender, data)
    if sender ~= nil then return end
    State.activeLap = {
        trackId = data.trackId,
        trackName = data.trackName,
        startTime = ac.getSim().time,
        totalCheckpoints = data.totalCheckpoints,
        currentCheckpoint = 0,
        pbMs = data.personalBestMs,
        valid = true,
        sectorTimes = {},
        totalTimeMs = 0
    }
    State.lastLapResult = nil
    State.lastInvalidation = nil
    ac.debug("TT_LapStart", data.trackName)
end)

local sectorCrossedEvent = ac.OnlineEvent({
    ac.StructItem.key("TT_SectorCrossed"),
    trackId = ac.StructItem.string(64),
    sectorIndex = ac.StructItem.int32(),
    sectorTimeMs = ac.StructItem.int32(),
    totalTimeMs = ac.StructItem.int32(),
    deltaToPbMs = ac.StructItem.int32(),
    isValid = ac.StructItem.boolean()
}, function(sender, data)
    if sender ~= nil then return end
    if State.activeLap and State.activeLap.trackId == data.trackId then
        State.activeLap.currentCheckpoint = data.sectorIndex
        State.activeLap.sectorTimes[data.sectorIndex] = {
            time = data.sectorTimeMs,
            delta = data.deltaToPbMs
        }
        State.activeLap.totalTimeMs = data.totalTimeMs
        State.activeLap.valid = data.isValid
    end
    ac.debug("TT_SectorCrossed", data.sectorIndex)
end)

local lapCompletedEvent = ac.OnlineEvent({
    ac.StructItem.key("TT_LapCompleted"),
    trackId = ac.StructItem.string(64),
    totalTimeMs = ac.StructItem.int32(),
    isPersonalBest = ac.StructItem.boolean(),
    leaderboardPosition = ac.StructItem.int32(),
    deltaToPbMs = ac.StructItem.int32(),
    sectorTimesJson = ac.StructItem.string(256)
}, function(sender, data)
    if sender ~= nil then return end
    State.lastLapResult = {
        trackId = data.trackId,
        totalTimeMs = data.totalTimeMs,
        isPb = data.isPersonalBest,
        position = data.leaderboardPosition,
        deltaToPbMs = data.deltaToPbMs,
        fadeAlpha = 1.0,
        displayTime = 8.0
    }
    if data.isPersonalBest then
        State.personalBests[data.trackId] = data.totalTimeMs
    end
    State.activeLap = nil
    ac.debug("TT_LapCompleted", data.totalTimeMs)
end)

local invalidationEvent = ac.OnlineEvent({
    ac.StructItem.key("TT_Invalidated"),
    trackId = ac.StructItem.string(64),
    reason = ac.StructItem.string(64)
}, function(sender, data)
    if sender ~= nil then return end
    State.lastInvalidation = {
        trackId = data.trackId,
        reason = data.reason,
        fadeAlpha = 1.0,
        displayTime = 3.0
    }
    if State.activeLap and State.activeLap.trackId == data.trackId then
        State.activeLap.valid = false
    end
    ac.debug("TT_Invalidated", data.reason)
end)

local leaderboardEvent = ac.OnlineEvent({
    ac.StructItem.key("TT_Leaderboard"),
    trackId = ac.StructItem.string(64),
    entriesJson = ac.StructItem.string(2048)
}, function(sender, data)
    if sender ~= nil then return end
    local ok, entries = pcall(JSON.parse, data.entriesJson)
    if ok and entries then
        State.leaderboards[data.trackId] = entries
    end
    ac.debug("TT_Leaderboard", data.trackId)
end)

local trackInfoEvent = ac.OnlineEvent({
    ac.StructItem.key("TT_TrackInfo"),
    tracksJson = ac.StructItem.string(1024),
    personalBestsJson = ac.StructItem.string(1024)
}, function(sender, data)
    if sender ~= nil then return end
    local ok1, tracks = pcall(JSON.parse, data.tracksJson)
    if ok1 and tracks then
        State.tracks = tracks
    end
    local ok2, pbs = pcall(JSON.parse, data.personalBestsJson)
    if ok2 and pbs then
        for _, pb in ipairs(pbs) do
            if pb.TrackId then
                State.personalBests[pb.TrackId] = pb.Time
            end
        end
    end
    ac.debug("TT_TrackInfo received")
end)

function script.update(dt)
    -- Update fading notifications
    if State.lastLapResult then
        State.lastLapResult.displayTime = State.lastLapResult.displayTime - dt
        if State.lastLapResult.displayTime < 2 then
            State.lastLapResult.fadeAlpha = math.max(0, State.lastLapResult.displayTime / 2)
        end
        if State.lastLapResult.fadeAlpha <= 0 then
            State.lastLapResult = nil
        end
    end

    if State.lastInvalidation then
        State.lastInvalidation.displayTime = State.lastInvalidation.displayTime - dt
        if State.lastInvalidation.displayTime < 1 then
            State.lastInvalidation.fadeAlpha = math.max(0, State.lastInvalidation.displayTime)
        end
        if State.lastInvalidation.fadeAlpha <= 0 then
            State.lastInvalidation = nil
        end
    end
end

function script.drawUI()
    if not Settings.hudEnabled then return end

    local uiState = ac.getUI()
    local sim = ac.getSim()

    local hasActiveLap = State.activeLap ~= nil
    local hasResult = State.lastLapResult ~= nil
    local hasInvalidation = State.lastInvalidation ~= nil

    -- Only show HUD if there's something to display
    if not hasActiveLap and not hasResult and not hasInvalidation then
        return
    end

    -- Calculate window size
    local dynamicHeight = 100
    if hasActiveLap then
        dynamicHeight = 120
    end
    if hasResult then
        dynamicHeight = 140
    end

    -- Position (top-right)
    local screenW, screenH = uiState.windowSize.x, uiState.windowSize.y
    local margin = 32
    local posX = screenW - UI.width - margin
    local posY = margin

    ui.transparentWindow("timeTrialHUD", vec2(posX, posY), vec2(UI.width, dynamicHeight), function()
        -- Background
        drawPanel(0, 0, UI.width, dynamicHeight, UI.cornerRadius, Theme.glass)

        local cx = UI.padding
        local cy = 10

        -- Active lap display
        if hasActiveLap then
            local lap = State.activeLap
            local validColor = lap.valid and Theme.textHero or Theme.error

            -- Track name
            ui.pushFont(ui.Font.Small)
            ui.setCursor(vec2(cx, cy))
            ui.textColored(lap.trackName or "Unknown Track", Theme.textMuted)
            cy = cy + 18
            ui.popFont()

            -- Calculate current elapsed time
            local elapsed = (sim.time - lap.startTime) * 1000

            -- Main timer
            ui.pushFont(ui.Font.Huge)
            ui.setCursor(vec2(cx, cy))
            ui.textColored(formatTime(math.floor(elapsed)), validColor)
            ui.popFont()
            cy = cy + 44

            -- Sector progress
            ui.pushFont(ui.Font.Small)
            ui.setCursor(vec2(cx, cy))
            local sectorText = string.format("SECTOR %d / %d", lap.currentCheckpoint + 1, lap.totalCheckpoints)
            ui.textColored(sectorText, Theme.textSecondary)
            ui.popFont()

        -- Lap completed display
        elseif hasResult then
            local result = State.lastLapResult
            local alpha = result.fadeAlpha

            local headerColor = result.isPb and Theme.success or Theme.accent
            headerColor = rgbm(headerColor.r, headerColor.g, headerColor.b, alpha)

            ui.pushFont(ui.Font.Main)
            ui.setCursor(vec2(cx, cy))
            ui.textColored(result.isPb and "NEW PERSONAL BEST!" or "LAP COMPLETE", headerColor)
            ui.popFont()
            cy = cy + 28

            -- Final time
            ui.pushFont(ui.Font.Huge)
            ui.setCursor(vec2(cx, cy))
            ui.textColored(formatTime(result.totalTimeMs), rgbm(Theme.textHero.r, Theme.textHero.g, Theme.textHero.b, alpha))
            ui.popFont()
            cy = cy + 46

            -- Delta
            if result.deltaToPbMs ~= 0 then
                local deltaColor = result.deltaToPbMs <= 0 and Theme.success or Theme.error
                deltaColor = rgbm(deltaColor.r, deltaColor.g, deltaColor.b, alpha)
                ui.pushFont(ui.Font.Main)
                ui.setCursor(vec2(cx, cy))
                ui.textColored(formatDelta(result.deltaToPbMs), deltaColor)
                ui.popFont()
            end

        -- Invalidation display
        elseif hasInvalidation then
            local inv = State.lastInvalidation
            local alpha = inv.fadeAlpha

            ui.pushFont(ui.Font.Main)
            ui.setCursor(vec2(cx, cy))
            ui.textColored("LAP INVALIDATED", rgbm(Theme.error.r, Theme.error.g, Theme.error.b, alpha))
            ui.popFont()
            cy = cy + 28

            ui.pushFont(ui.Font.Small)
            ui.setCursor(vec2(cx, cy))
            ui.textColored(inv.reason or "Unknown", rgbm(Theme.textSecondary.r, Theme.textSecondary.g, Theme.textSecondary.b, alpha))
            ui.popFont()
        end
    end)
end
