local msg = require("mp.msg")
local options = require("mp.options")

local settings = {
    speeds = "0.10,0.20,0.25,0.50,0.75,0.80,0.90,1.00,1.10,1.20,1.25,1.50,1.75,2.00,2.25,2.50,3.00,3.50,4.00,5.00,6.00,8.00,10.00",
    default_speed = 2.0,

    increase_key = "Ctrl+WHEEL_UP",
    decrease_key = "Ctrl+WHEEL_DOWN",
    reset_key = "Ctrl+0",

    osd_duration = 1.2,
}

options.read_options(settings, "hold_speed")

local speeds = {}

for value in settings.speeds:gmatch("[^,%s]+") do
    local speed = tonumber(value)

    if speed and speed > 0 then
        table.insert(speeds, speed)
    end
end

table.sort(speeds)

local unique_speeds = {}

for _, speed in ipairs(speeds) do
    if unique_speeds[#unique_speeds] ~= speed then
        table.insert(unique_speeds, speed)
    end
end

speeds = unique_speeds

if #speeds == 0 then
    speeds = { 2.0 }
end

local function nearest_index(target)
    local best_index = 1
    local best_difference = math.huge

    for index, speed in ipairs(speeds) do
        local difference = math.abs(speed - target)

        if difference < best_difference then
            best_index = index
            best_difference = difference
        end
    end

    return best_index
end

local separator = package.config:sub(1, 1)

-- mp.get_script_directory() can return nil in some mpv launch/config layouts.
-- The old direct concatenation crashed the entire script before any key
-- binding or OSD message could be registered.
local function get_state_path()
    local ok_expand, expanded = pcall(
        mp.command_native,
        { "expand-path", "~~/hold-speed-state.txt" }
    )

    if ok_expand and type(expanded) == "string" and expanded ~= "" then
        return expanded
    end

    if mp.get_script_directory then
        local ok_dir, script_dir = pcall(mp.get_script_directory)

        if ok_dir and type(script_dir) == "string" and script_dir ~= "" then
            return script_dir .. separator .. "hold-speed-state.txt"
        end
    end

    -- Last-resort fallback: save beside mpv's current working directory.
    return "hold-speed-state.txt"
end

local state_path = get_state_path()

local function load_saved_speed()
    local file = io.open(state_path, "r")
    if not file then
        return nil
    end

    local value = tonumber(file:read("*l"))
    file:close()
    return value
end

-- NOTE: all state locals below must be declared BEFORE any function that
-- references them, or Lua will treat the reference as a global (which is
-- always nil) instead of capturing the real local as an upvalue. This was
-- the cause of a silent crash in save_selected_speed() every time the
-- speed was changed.
local saved_speed = load_saved_speed()
local selected_index = nearest_index(saved_speed or settings.default_speed)
local active = false
local latched_after_cancel = false
local previous_speed = nil
local last_side_event_time = -10
local last_side_event_type = ""

local function save_selected_speed()
    local file = io.open(state_path, "w")
    if not file then
        return
    end

    file:write(string.format("%.6f", speeds[selected_index]))
    file:close()
end

local function format_speed(speed)
    local text = string.format("%.2f", speed or 1.0)
    text = text:gsub("0+$", "")
    text = text:gsub("%.$", "")
    return text .. "x"
end

local function show_selected_speed()
    mp.osd_message(
        "Hold speed: " .. format_speed(speeds[selected_index]),
        settings.osd_duration
    )
end

local function activate_hold()
    if not active then
        previous_speed = mp.get_property_number("speed", 1.0)
        active = true
    end

    mp.set_property_number("speed", speeds[selected_index])
    mp.osd_message(
        "Speed boost: " .. format_speed(speeds[selected_index]),
        settings.osd_duration
    )
end

local function restore_speed()
    if not active then
        return
    end

    active = false
    latched_after_cancel = false

    local restored = previous_speed or 1.0
    mp.set_property_number("speed", restored)
    previous_speed = nil

    mp.osd_message(
        "Speed: " .. format_speed(restored),
        settings.osd_duration
    )
end

local function change_selected_speed(direction)
    selected_index = math.max(
        1,
        math.min(#speeds, selected_index + direction)
    )

    if active then
        mp.set_property_number("speed", speeds[selected_index])
    end

    save_selected_speed()
    show_selected_speed()
end

local function reset_selected_speed()
    selected_index = nearest_index(settings.default_speed)

    if active then
        mp.set_property_number("speed", speeds[selected_index])
    end

    save_selected_speed()
    show_selected_speed()
end

local function reset_playback_speed()
    -- Guaranteed emergency reset for a boost that was left active by a mouse
    -- driver or an interrupted event sequence. This intentionally does not
    -- change the selected hold-speed preset.
    active = false
    latched_after_cancel = false
    previous_speed = nil
    mp.set_property_number("speed", 1.0)
    mp.osd_message("Speed: 1x", settings.osd_duration)
end

local function side_button_handler(event)
    local event_type = event.event or "unknown"
    local now = mp.get_time()

    -- Diagnostic: run mpv with --msg-level=all=v (or check the log file) to
    -- confirm the button press is actually reaching mpv at all. If nothing
    -- ever prints here when you click the side button, mpv is not receiving
    -- the event -- almost always because Stremio's Electron/Chromium shell
    -- (or the OS mouse driver) is intercepting MBTN_BACK/MBTN_FORWARD as a
    -- browser-style back/forward navigation before it reaches the embedded
    -- mpv window. That is outside what this script can fix; try remapping
    -- the physical button to a normal key (e.g. via mouse software) instead.
    msg.verbose("hold-speed: side button event=" .. event_type ..
        " canceled=" .. tostring(event.canceled))

    -- Ignore duplicate alias events from the same physical button.
    if event_type == last_side_event_type and
       (now - last_side_event_time) < 0.08 then
        return
    end

    last_side_event_time = now
    last_side_event_type = event_type

    -- Ctrl or wheel input can logically cancel a held mouse binding in mpv.
    -- Keep the boost active so Ctrl+wheel can still adjust it.
    if event.canceled then
        if active then
            latched_after_cancel = true
        end
        return
    end

    if event_type == "down" then
        if active and latched_after_cancel then
            restore_speed()
        else
            latched_after_cancel = false
            activate_hold()
        end
        return
    end

    if event_type == "up" then
        restore_speed()
        return
    end

    -- Fallback for mouse drivers that expose only press events.
    if event_type == "press" then
        if active then
            restore_speed()
        else
            latched_after_cancel = true
            activate_hold()
        end
    end
end

-- Wrap every binding in pcall so a single invalid/unsupported key name
-- (or a busy binding name) can't silently abort the rest of the script's
-- setup -- this used to be a real risk with the old "BACK"/"FORWARD"
-- pseudo-aliases below, which are not valid mpv input names and have been
-- removed.
local function safe_bind(key_name, binding_name, fn, flags)
    local ok, err = pcall(mp.add_forced_key_binding, key_name, binding_name, fn, flags)
    if not ok then
        msg.warn("hold-speed: failed to bind '" .. key_name .. "' (" ..
            binding_name .. "): " .. tostring(err))
    end
end

-- Input testing confirmed the physical side button is exactly MBTN_BACK.
-- Bind only that key so Mouse Forward cannot interfere.
safe_bind(
    "MBTN_BACK",
    "hold-speed-mbtn-back",
    side_button_handler,
    { complex = true }
)

msg.info("hold-speed: MBTN_BACK binding registered")

safe_bind(
    settings.increase_key,
    "increase-hold-speed",
    function()
        change_selected_speed(1)
    end
)

safe_bind(
    settings.decrease_key,
    "decrease-hold-speed",
    function()
        change_selected_speed(-1)
    end
)

safe_bind(
    settings.reset_key,
    "reset-hold-speed",
    reset_selected_speed
)

safe_bind(
    "BS",
    "reset-playback-speed-to-one",
    reset_playback_speed
)

safe_bind(
    "F7",
    "show-hold-speed-status",
    function()
        mp.osd_message(
            "Speed control ready | Hold speed: " ..
            format_speed(speeds[selected_index]) ..
            " | Playback: " ..
            format_speed(mp.get_property_number("speed", 1.0)),
            2.5
        )
    end
)

mp.register_event("file-loaded", function()
    mp.osd_message(
        "Speed control ready - hold Mouse Back (" ..
        format_speed(speeds[selected_index]) ..
        ")",
        2.0
    )
end)

mp.register_event("end-file", function()
    -- Restore before clearing state so a held/toggled boost can never leak
    -- into the next playlist entry.
    if active then
        mp.set_property_number("speed", previous_speed or 1.0)
    end

    active = false
    latched_after_cancel = false
    previous_speed = nil
end)

mp.register_event("shutdown", function()
    if active and previous_speed then
        mp.set_property_number("speed", previous_speed)
    end
end)
