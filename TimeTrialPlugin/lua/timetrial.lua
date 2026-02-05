--[[
Time Trial Plugin - Client UI
Displays lap timing, sector splits, personal bests, and leaderboard
Theme: Obsidian Glass (matching swim-assetto-lua style)
]]

local config = ac.configValues({
    showLeaderboard = true,
    leaderboardSize = 10
})

-- Theme (Obsidian Glass)
local Theme = {
    glass = rgbm(0.047, 0.047, 0.059, 0.85),
    glassHighlight = rgbm(1, 1, 1, 0.04),
    glassEdge = rgbm(1, 1, 1, 0.08),
    glassSurface = rgbm(0.071, 0.071, 0.086, 0.72),
    textHero = rgbm(1, 1, 1, 1),
    textPrimary = rgbm(0.941, 0.941, 0.949, 1),
    textSecondary = rgbm(0.659, 0.659, 0.690, 1),
    textMuted = rgbm(0.376, 0.376, 0.408, 1),
    textGhost = rgbm(0.251, 0.251, 0.282, 1),
    accent = rgbm(0.647, 0.769, 0.831, 1),
    accentBright = rgbm(0.761, 0.855, 0.902, 1),
    accentGlow = rgbm(0.647, 0.769, 0.831, 0.25),
    accentSubtle = rgbm(0.647, 0.769, 0.831, 0.08),
    success = rgbm(0.639, 0.902, 0.208, 1),
    successGlow = rgbm(0.639, 0.902, 0.208, 0.2),
    error = rgbm(0.973, 0.443, 0.443, 1),
    errorGlow = rgbm(0.973, 0.443, 0.443, 0.2),
    warning = rgbm(1.0, 0.8, 0.2, 1),
}

local UI = {
    width = 240,
    padding = 14,
    cornerRadius = 12,
    pulsePhase = 0,
}

local Settings = {
    hudEnabled = true,
    hudPosition = 2, -- 1=top-left, 2=top-right, 3=bottom-left, 4=bottom-right
    showLeaderboard = config.showLeaderboard
}

-- State
local State = {
    tracks = {},
    personalBests = {},
    leaderboards = {},
    activeLap = nil, -- { trackId, trackName, startTime, totalCheckpoints, currentCheckpoint, pbMs, valid, sectorTimes }
    lastLapResult = nil, -- { trackId, totalTimeMs, isPb, position, deltaToPbMs, fadeAlpha }
    lastInvalidation = nil, -- { trackId, reason, fadeAlpha }
    clientStartTime = 0,
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

local function drawPanel(x, y, w, h, radius, fillColor, borderColor)
    local p1, p2 = vec2(x, y), vec2(x + w, y + h)
    ui.drawRectFilled(p1, p2, fillColor, radius)
    if borderColor then
        ui.drawRect(p1, p2, borderColor, radius, 1)
    end
end

local function drawGlowStrip(x, y, width, color)
    local centerX = x + width / 2
    local stripWidth = width * 0.6
    ui.drawRectFilled(vec2(centerX - stripWidth / 2, y), vec2(centerX + stripWidth / 2, y + 2), color)
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
end)

local leaderboardEvent = ac.OnlineEvent({
    ac.StructItem.key("TT_Leaderboard"),
    trackId = ac.StructItem.string(64),
    entriesJson = ac.StructItem.string(2048)
}, function(sender, data)
    if sender ~= nil then return end
    local entries = JSON.parse(data.entriesJson)
    if entries then
        State.leaderboards[data.trackId] = entries
    end
end)

local trackInfoEvent = ac.OnlineEvent({
    ac.StructItem.key("TT_TrackInfo"),
    tracksJson = ac.StructItem.string(1024),
    personalBestsJson = ac.StructItem.string(1024)
}, function(sender, data)
    if sender ~= nil then return end
    local tracks = JSON.parse(data.tracksJson)
    if tracks then
        State.tracks = tracks
    end
    local pbs = JSON.parse(data.personalBestsJson)
    if pbs then
        for _, pb in ipairs(pbs) do
            State.personalBests[pb.TrackId] = pb.Time
        end
    end
end)

-- Settings menu
local function timeTrialExtrasUI()
    local hudLabel = Settings.hudEnabled and "Hide Timer" or "Show Timer"
    if ui.button(hudLabel, vec2(140, 0)) then
        Settings.hudEnabled = not Settings.hudEnabled
    end

    local lbLabel = Settings.showLeaderboard and "Hide Leaderboard" or "Show Leaderboard"
    if ui.button(lbLabel, vec2(140, 0)) then
        Settings.showLeaderboard = not Settings.showLeaderboard
    end

    ui.offsetCursorY(8)
    ui.text("Position")

    if ui.button("Top-Left", vec2(68, 0)) then Settings.hudPosition = 1 end
    ui.sameLine(0, 4)
    if ui.button("Top-Right", vec2(68, 0)) then Settings.hudPosition = 2 end

    if ui.button("Bot-Left", vec2(68, 0)) then Settings.hudPosition = 3 end
    ui.sameLine(0, 4)
    if ui.button("Bot-Right", vec2(68, 0)) then Settings.hudPosition = 4 end
end

ui.registerOnlineExtra(ui.Icons.Stopwatch, "Time Trial", nil, timeTrialExtrasUI, nil)

function script.update(dt)
    UI.pulsePhase = (UI.pulsePhase + dt * 1.5) % (math.pi * 2)

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

    -- Calculate pulse accent
    local accentPulse = 0.9 + math.sin(UI.pulsePhase) * 0.1
    local accentColor = rgbm(Theme.accent.r * accentPulse, Theme.accent.g * accentPulse, Theme.accent.b * accentPulse, 1)

    -- Calculate window size
    local dynamicHeight = 120
    if hasActiveLap then
        dynamicHeight = 140 + (State.activeLap.currentCheckpoint * 20)
    end
    if hasResult then
        dynamicHeight = 160
    end

    -- Position
    local screenW, screenH = uiState.windowSize.x, uiState.windowSize.y
    local margin = 32
    local posX, posY

    if Settings.hudPosition == 1 then
        posX, posY = margin, margin
    elseif Settings.hudPosition == 2 then
        posX, posY = screenW - UI.width - margin, margin
    elseif Settings.hudPosition == 3 then
        posX, posY = margin, screenH - dynamicHeight - margin
    else
        posX, posY = screenW - UI.width - margin, screenH - dynamicHeight - margin
    end

    ui.beginTransparentWindow("timeTrialHUD", vec2(posX, posY), vec2(UI.width, dynamicHeight), true)

    -- Background
    drawPanel(0, 0, UI.width, dynamicHeight, UI.cornerRadius, Theme.glass, nil)
    drawGlowStrip(0, 0, UI.width, accentColor)

    local cx = UI.padding
    local cy = 8

    -- Active lap display
    if hasActiveLap then
        local lap = State.activeLap
        local validColor = lap.valid and Theme.textHero or Theme.error

        -- Track name
        ui.pushFont(ui.Font.Small)
        ui.setCursor(vec2(cx, cy))
        ui.textColored(lap.trackName, Theme.textMuted)
        cy = cy + 16

        -- Calculate current elapsed time
        local elapsed = (sim.time - lap.startTime) * 1000
        if lap.totalTimeMs > 0 then
            elapsed = lap.totalTimeMs + ((sim.time - lap.startTime) * 1000 - lap.totalTimeMs)
        end

        -- Main timer (large)
        ui.popFont()
        ui.pushFont(ui.Font.Huge)
        ui.setCursor(vec2(cx, cy))
        ui.textColored(formatTime(math.floor(elapsed)), validColor)
        ui.popFont()
        cy = cy + 42

        -- Delta to PB
        if lap.pbMs > 0 then
            local delta = elapsed - lap.pbMs
            local deltaColor = delta <= 0 and Theme.success or Theme.error
            ui.pushFont(ui.Font.Main)
            ui.setCursor(vec2(cx, cy))
            ui.textColored(formatDelta(math.floor(delta)), deltaColor)
            ui.popFont()
            cy = cy + 24
        end

        -- Sector progress
        ui.pushFont(ui.Font.Small)
        ui.setCursor(vec2(cx, cy))
        local sectorText = string.format("SECTOR %d / %d", lap.currentCheckpoint + 1, lap.totalCheckpoints)
        ui.textColored(sectorText, Theme.textSecondary)
        cy = cy + 16

        -- Sector times
        for i, sector in pairs(lap.sectorTimes) do
            local deltaColor = sector.delta <= 0 and Theme.success or Theme.error
            ui.setCursor(vec2(cx, cy))
            ui.textColored(string.format("S%d: %s", i, formatTime(sector.time)), Theme.textPrimary)
            if sector.delta ~= 0 then
                ui.sameLine(0, 8)
                ui.textColored(formatDelta(sector.delta), deltaColor)
            end
            cy = cy + 16
        end
        ui.popFont()

        -- Invalid indicator
        if not lap.valid then
            ui.pushFont(ui.Font.Main)
            ui.setCursor(vec2(cx, cy))
            ui.textColored("INVALID", Theme.error)
            ui.popFont()
        end

    -- Lap completed display
    elseif hasResult then
        local result = State.lastLapResult
        local alpha = result.fadeAlpha

        -- Track name (find from state)
        local trackName = result.trackId
        for _, t in ipairs(State.tracks) do
            if t.Id == result.trackId then
                trackName = t.Name
                break
            end
        end

        ui.pushFont(ui.Font.Small)
        ui.setCursor(vec2(cx, cy))
        ui.textColored(trackName, rgbm(Theme.textMuted.r, Theme.textMuted.g, Theme.textMuted.b, alpha))
        cy = cy + 16
        ui.popFont()

        -- Lap complete header
        local headerColor = result.isPb and Theme.success or Theme.accent
        headerColor = rgbm(headerColor.r, headerColor.g, headerColor.b, alpha)

        ui.pushFont(ui.Font.Main)
        ui.setCursor(vec2(cx, cy))
        ui.textColored(result.isPb and "NEW PERSONAL BEST!" or "LAP COMPLETE", headerColor)
        ui.popFont()
        cy = cy + 24

        -- Final time
        ui.pushFont(ui.Font.Huge)
        ui.setCursor(vec2(cx, cy))
        ui.textColored(formatTime(result.totalTimeMs), rgbm(Theme.textHero.r, Theme.textHero.g, Theme.textHero.b, alpha))
        ui.popFont()
        cy = cy + 44

        -- Delta and position
        ui.pushFont(ui.Font.Main)
        if result.deltaToPbMs ~= 0 then
            local deltaColor = result.deltaToPbMs <= 0 and Theme.success or Theme.error
            deltaColor = rgbm(deltaColor.r, deltaColor.g, deltaColor.b, alpha)
            ui.setCursor(vec2(cx, cy))
            ui.textColored(formatDelta(result.deltaToPbMs), deltaColor)
            cy = cy + 22
        end

        if result.position > 0 then
            ui.setCursor(vec2(cx, cy))
            ui.textColored(string.format("Position: #%d", result.position), rgbm(Theme.textSecondary.r, Theme.textSecondary.g, Theme.textSecondary.b, alpha))
        end
        ui.popFont()

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
        ui.textColored(inv.reason, rgbm(Theme.textSecondary.r, Theme.textSecondary.g, Theme.textSecondary.b, alpha))
        ui.popFont()
    end

    ui.endTransparentWindow()

    -- Leaderboard window (optional, separate panel)
    if Settings.showLeaderboard and State.activeLap then
        local trackId = State.activeLap.trackId
        local leaderboard = State.leaderboards[trackId]
        if leaderboard and #leaderboard > 0 then
            local lbHeight = 30 + (#leaderboard * 18)
            local lbWidth = 200
            local lbX = posX - lbWidth - 16
            if Settings.hudPosition == 1 or Settings.hudPosition == 3 then
                lbX = posX + UI.width + 16
            end
            local lbY = posY

            ui.beginTransparentWindow("timeTrialLeaderboard", vec2(lbX, lbY), vec2(lbWidth, lbHeight), true)
            drawPanel(0, 0, lbWidth, lbHeight, 10, Theme.glass, nil)

            ui.pushFont(ui.Font.Small)
            ui.setCursor(vec2(10, 8))
            ui.textColored("LEADERBOARD", Theme.textMuted)

            local ly = 26
            for i, entry in ipairs(leaderboard) do
                ui.setCursor(vec2(10, ly))
                ui.textColored(string.format("%d.", i), Theme.textGhost)
                ui.sameLine(0, 4)
                ui.textColored(entry.PlayerName or "Unknown", Theme.textPrimary)
                ui.setCursor(vec2(lbWidth - 60, ly))
                ui.textColored(entry.FormattedTime or formatTime(entry.TotalTimeMs), Theme.textSecondary)
                ly = ly + 18
            end
            ui.popFont()

            ui.endTransparentWindow()
        end
    end
end
