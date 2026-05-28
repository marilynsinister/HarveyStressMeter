using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using HarveyStressMeter.Helpers;
using StardewValley;

namespace HarveyStressMeter.Testing
{
    /// <summary>MCP tools for game environment control (time, weather, warp).</summary>
    internal static class McpEnvironmentTools
    {
        private static readonly Dictionary<string, (int X, int Y)> DefaultTiles =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["FarmHouse"] = (5, 5),
                ["Hospital"] = (16, 10),
                ["Forest"] = (48, 14),
                ["Woods"] = (48, 14),
                ["Town"] = (47, 57),
                ["Mountain"] = (32, 11),
                ["Mine"] = (14, 10),
            };

        private static readonly HashSet<string> AllowedWeather =
            new(StringComparer.OrdinalIgnoreCase) { "sun", "rain", "storm", "snow", "wind" };

        public static string SetTime(JsonElement? arguments)
        {
            if (EnvironmentBlockMessage("mcp_set_time") is { } blocked)
                return blocked;

            if (!TryGetTimeArgument(arguments, "time", out var newTime))
                return "Error: time is required (Stardew format 600–2600, 10-minute steps).";

            if (!IsValidStardewTime(newTime))
                return $"Error: invalid Stardew time '{newTime}'. Use 600–2600 with minutes 00–50 in steps of 10.";

            var oldTime = Game1.timeOfDay;
            Game1.timeOfDay = newTime;

            return BuildTimeResult(oldTime, newTime, warning: null);
        }

        public static string AddMinutes(JsonElement? arguments)
        {
            if (EnvironmentBlockMessage("mcp_add_minutes") is { } blocked)
                return blocked;

            if (!TryGetInt(arguments, "minutes", out var minutes))
                return "Error: minutes is required.";

            if (minutes < 0)
                return "Error: minutes must be >= 0.";

            var oldTime = Game1.timeOfDay;
            if (!IsValidStardewTime(oldTime))
                return $"Error: current game time '{oldTime}' is not a valid Stardew time; use mcp_set_time first.";

            var newTime = AddStardewMinutes(oldTime, minutes);
            Game1.timeOfDay = newTime;

            var sb = new StringBuilder();
            sb.AppendLine(BuildTimeResult(oldTime, newTime, warning:
                "time changed directly; not all tick-based systems may have updated."));
            return sb.ToString().TrimEnd();
        }

        public static string SetWeather(JsonElement? arguments)
        {
            if (EnvironmentBlockMessage("mcp_set_weather") is { } blocked)
                return blocked;

            if (!TryGetString(arguments, "weather", out var weatherRaw))
                return "Error: weather is required (sun|rain|storm|snow|wind).";

            var weather = weatherRaw.Trim().ToLowerInvariant();
            if (!AllowedWeather.Contains(weather))
                return $"Error: weather '{weatherRaw}' is not allowed. Use sun, rain, storm, snow, or wind.";

            ApplyWeather(weather);

            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"weather: {weather}");
            sb.AppendLine($"isRaining: {Game1.isRaining}");
            sb.AppendLine($"isLightning: {Game1.isLightning}");
            sb.AppendLine($"isSnowing: {Game1.isSnowing}");
            sb.AppendLine($"isDebrisWeather: {Game1.isDebrisWeather}");
            sb.AppendLine($"location: {GetLocationName()}");
            sb.AppendLine($"date: {FormatDate()}");
            return sb.ToString().TrimEnd();
        }

        /// <summary>Fallback if wait is routed through handler instead of McpWaitService.</summary>
        public static string WaitSecondsNotSupportedHere()
            => "Error: mcp_wait_seconds must be handled by McpWaitService (deferred tick wait).";

        public static string Warp(JsonElement? arguments)
        {
            if (EnvironmentBlockMessage("mcp_warp") is { } blocked)
                return blocked;

            if (!TryGetString(arguments, "location", out var locationName))
                return "Error: location is required.";

            if (!TryResolveLocation(locationName, out var targetLocation, out var resolveError))
                return resolveError;

            var oldLocation = GetLocationName();
            var warpName = targetLocation!.NameOrUniqueName ?? targetLocation.Name ?? locationName;

            if (!TryResolveWarpTile(locationName, warpName, arguments, out var x, out var y, out var tileError))
                return tileError;

            if (!McpWarpHelper.TryWarpImmediate(warpName, x, y, out var resolvedX, out var resolvedY, out var warpError))
                return warpError ?? "Error: warp failed.";

            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"oldLocation: {oldLocation}");
            sb.AppendLine($"newLocation: {GetLocationName()}");
            sb.AppendLine($"tile: {resolvedX},{resolvedY}");
            if (resolvedX != x || resolvedY != y)
                sb.AppendLine($"requestedTile: {x},{y}");
            sb.AppendLine($"warpTarget: {warpName}");
            sb.AppendLine("fadeWaitMs: 0");
            sb.AppendLine("mode: immediate");
            sb.AppendLine($"isSitting: {Game1.player.IsSitting()}");

            if (GameStateHelper.IsEventActive())
            {
                sb.AppendLine(
                    $"warning: event active after warp (EventId={GameStateHelper.GetCurrentEventId() ?? "unknown"}).");
            }

            return sb.ToString().TrimEnd();
        }

        public static string? EnvironmentBlockMessage(string operation)
        {
            if (GameStateHelper.IsEventActive())
            {
                var eventId = GameStateHelper.GetCurrentEventId() ?? "unknown";
                return $"Error: event active (EventId={eventId}). {operation} blocked.\n\n{TestContextReporter.BuildReport()}";
            }

            if (Game1.activeClickableMenu != null)
            {
                return
                    $"Error: menu active ({Game1.activeClickableMenu.GetType().Name}). {operation} blocked.\n\n{TestContextReporter.BuildReport()}";
            }

            if (GameStateHelper.IsEnvironmentDialogueBlocking())
            {
                return $"Error: dialogue active. {operation} blocked.\n\n{TestContextReporter.BuildReport()}";
            }

            return null;
        }

        internal static bool IsValidStardewTime(int time)
        {
            if (time < 600 || time > 2600)
                return false;

            var minutes = time % 100;
            if (minutes >= 60 || minutes % 10 != 0)
                return false;

            var hours = time / 100;
            if (hours == 26)
                return minutes == 0;

            if (hours >= 24)
                return hours <= 25;

            return hours >= 6;
        }

        internal static int AddStardewMinutes(int time, int minutesToAdd)
        {
            var total = ToMinutesSince6Am(time) + minutesToAdd;
            return FromMinutesSince6Am(total);
        }

        private static int ToMinutesSince6Am(int time)
        {
            if (time >= 2400)
                return (time / 100 - 24) * 60 + (time % 100) + 18 * 60;

            return (time / 100 - 6) * 60 + (time % 100);
        }

        private static int FromMinutesSince6Am(int totalMinutes)
        {
            totalMinutes = Math.Clamp(totalMinutes, 0, 20 * 60);

            if (totalMinutes >= 18 * 60)
            {
                var afterMidnight = totalMinutes - 18 * 60;
                if (afterMidnight >= 120)
                    return 2600;

                return 2400 + (afterMidnight / 60) * 100 + (afterMidnight % 60);
            }

            var hour = 6 + totalMinutes / 60;
            var minute = totalMinutes % 60;
            return hour * 100 + minute;
        }

        private static void ApplyWeather(string weather)
        {
            Game1.isRaining = false;
            Game1.isLightning = false;
            Game1.isSnowing = false;
            Game1.isDebrisWeather = false;

            switch (weather)
            {
                case "rain":
                    Game1.isRaining = true;
                    break;
                case "storm":
                    Game1.isRaining = true;
                    Game1.isLightning = true;
                    break;
                case "snow":
                    Game1.isSnowing = true;
                    break;
                case "wind":
                    Game1.isDebrisWeather = true;
                    break;
            }
        }

        internal static string WarpToLocation(string locationName, int? x = null, int? y = null)
        {
            if (!TryResolveLocation(locationName, out var targetLocation, out var resolveError))
                return resolveError;

            var oldLocation = GetLocationName();
            var warpName = targetLocation!.NameOrUniqueName ?? targetLocation.Name ?? locationName;

            int tileX;
            int tileY;
            if (x.HasValue && y.HasValue)
            {
                tileX = x.Value;
                tileY = y.Value;
            }
            else if (TryGetDefaultTile(warpName, out var tile) || TryGetDefaultTile(locationName, out tile))
            {
                tileX = tile.X;
                tileY = tile.Y;
            }
            else
            {
                tileX = 5;
                tileY = 5;
            }

            if (!McpWarpHelper.TryWarpImmediate(warpName, tileX, tileY, out var warpError))
                return warpError ?? "Error: warp failed.";

            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"oldLocation: {oldLocation}");
            sb.AppendLine($"newLocation: {GetLocationName()}");
            sb.AppendLine($"tile: {tileX},{tileY}");
            sb.AppendLine($"warpTarget: {warpName}");
            sb.AppendLine("mode: immediate");
            return sb.ToString().TrimEnd();
        }

        internal static bool TryGetDefaultTile(string locationName, out (int X, int Y) tile)
        {
            if (DefaultTiles.TryGetValue(locationName, out tile))
                return true;

            if (!TryResolveLocation(locationName, out var location, out _))
            {
                tile = default;
                return false;
            }

            var resolved = location!.NameOrUniqueName ?? location.Name ?? locationName;
            return DefaultTiles.TryGetValue(resolved, out tile);
        }

        private static bool TryResolveWarpTile(
            string locationName,
            string warpName,
            JsonElement? arguments,
            out int x,
            out int y,
            out string error)
        {
            error = string.Empty;
            x = 0;
            y = 0;

            if (TryGetInt(arguments, "x", out var argX) && TryGetInt(arguments, "y", out var argY))
            {
                x = argX;
                y = argY;
                return true;
            }

            if (TryGetDefaultTile(warpName, out var tile) || TryGetDefaultTile(locationName, out tile))
            {
                x = tile.X;
                y = tile.Y;
                return true;
            }

            x = 5;
            y = 5;
            return true;
        }

        internal static bool TryResolveLocation(string name, out GameLocation? location, out string error)
        {
            location = null;
            error = "";

            if (string.IsNullOrWhiteSpace(name))
            {
                error = "Error: location name is empty.";
                return false;
            }

            location = Game1.getLocationFromName(name);
            if (location != null)
                return true;

            foreach (var candidate in Game1.locations)
            {
                if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.NameOrUniqueName, name, StringComparison.OrdinalIgnoreCase))
                {
                    location = candidate;
                    return true;
                }
            }

            error = $"Error: location '{name}' not found.";
            return false;
        }

        private static string BuildTimeResult(int oldTime, int newTime, string? warning)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"oldTime: {oldTime}");
            sb.AppendLine($"newTime: {newTime}");
            sb.AppendLine($"date: {FormatDate()}");
            sb.AppendLine($"location: {GetLocationName()}");
            sb.AppendLine($"eventActive: {GameStateHelper.IsEventActive()}");
            sb.AppendLine($"menuActive: {Game1.activeClickableMenu != null}");
            if (!string.IsNullOrEmpty(warning))
                sb.AppendLine($"warning: {warning}");
            return sb.ToString().TrimEnd();
        }

        private static string GetLocationName()
            => Game1.currentLocation?.NameOrUniqueName ?? Game1.currentLocation?.Name ?? "null";

        private static string FormatDate()
            => $"{Game1.currentSeason} {Game1.dayOfMonth} Year {Game1.year}";

        private static bool TryGetTimeArgument(JsonElement? arguments, string name, out int value)
        {
            value = 0;
            if (arguments is not { ValueKind: JsonValueKind.Object } args
                || !args.TryGetProperty(name, out var el))
            {
                return false;
            }

            if (el.ValueKind == JsonValueKind.Number)
                return el.TryGetInt32(out value);

            if (el.ValueKind == JsonValueKind.String
                && int.TryParse(el.GetString(), out value))
            {
                return true;
            }

            return false;
        }

        private static bool TryGetString(JsonElement? arguments, string name, out string value)
        {
            value = string.Empty;
            if (arguments is not { ValueKind: JsonValueKind.Object } args
                || !args.TryGetProperty(name, out var el)
                || el.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            value = el.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool TryGetInt(JsonElement? arguments, string name, out int value)
        {
            value = 0;
            if (arguments is not { ValueKind: JsonValueKind.Object } args
                || !args.TryGetProperty(name, out var el))
            {
                return false;
            }

            return el.TryGetInt32(out value);
        }
    }
}
