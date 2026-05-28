using System.Collections.Generic;
using HarveyStressMeter.Models;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using xTile.Layers;

namespace HarveyStressMeter.Helpers
{
    /// <summary>
    /// Сохраняет и восстанавливает позицию игрока после Gotoro forest rescue.
    /// Опирается на проходимость runtime-карты (TMX-паспорта: Forest 48,14; Woods 48,14).
    /// </summary>
    public static class RescuePlayerPositionHelper
    {
        private static readonly Dictionary<string, Point> FallbackTiles =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Forest"] = new(48, 14),
                ["Woods"] = new(48, 14),
                ["SecretWoods"] = new(48, 14),
                ["Town"] = new(47, 57),
            };

        /// <summary>Find a passable non-seat tile near preferred (for MCP debug warps).</summary>
        public static bool TryFindSafeStandingTile(GameLocation location, Point preferred, out Point safeTile)
        {
            var found = FindSafeTile(location, preferred);
            if (found == null)
            {
                safeTile = default;
                return false;
            }

            safeTile = found.Value;
            return true;
        }

        public static void CaptureTriggerPosition(HarveyFlashbackRescueState state)
        {
            var location = Game1.currentLocation;
            if (location == null)
                return;

            state.RescueTriggerLocation = location.NameOrUniqueName ?? location.Name;
            state.RescueTriggerTileX = (int)Game1.player.Tile.X;
            state.RescueTriggerTileY = (int)Game1.player.Tile.Y;
        }

        public static bool TryRestorePlayerPosition(
            HarveyFlashbackRescueState state,
            IMonitor monitor,
            out string summary)
        {
            summary = "no_restore_needed";

            if (string.IsNullOrEmpty(state.RescueTriggerLocation))
                return false;

            if (GameStateHelper.IsEventActive() || Game1.dialogueUp)
                return false;

            var targetLocationName = state.RescueTriggerLocation;
            var preferred = new Point(state.RescueTriggerTileX, state.RescueTriggerTileY);
            var currentLocationName = Game1.currentLocation?.NameOrUniqueName
                ?? Game1.currentLocation?.Name
                ?? "";

            var needsRelocation = IsUnexpectedRescueExitLocation(targetLocationName, currentLocationName)
                || !IsSafeStandingTile(Game1.currentLocation, Game1.player.Tile);

            if (!needsRelocation)
            {
                ClearCapture(state);
                summary = "position_ok";
                return true;
            }

            if (!TryResolveLocation(targetLocationName, out var targetLocation))
            {
                monitor.Log(
                    $"[FlashbackRescue] Restore failed: location '{targetLocationName}' not found.",
                    LogLevel.Warn);
                summary = "location_not_found";
                ClearCapture(state);
                return false;
            }

            var safeTile = FindSafeTile(targetLocation!, preferred);
            if (safeTile == null)
            {
                monitor.Log(
                    $"[FlashbackRescue] Restore failed: no safe tile in '{targetLocationName}' near {preferred.X},{preferred.Y}.",
                    LogLevel.Warn);
                summary = "no_safe_tile";
                ClearCapture(state);
                return false;
            }

            Game1.player.ignoreCollisions = false;
            var warpName = targetLocation!.NameOrUniqueName ?? targetLocation.Name ?? targetLocationName;
            Game1.warpFarmer(warpName, safeTile.Value.X, safeTile.Value.Y, Game1.player.FacingDirection);

            monitor.Log(
                $"[FlashbackRescue] Restored player to {warpName} ({safeTile.Value.X},{safeTile.Value.Y}); " +
                $"was {currentLocationName} ({Game1.player.Tile.X},{Game1.player.Tile.Y}) before fix; " +
                $"trigger={preferred.X},{preferred.Y}",
                LogLevel.Info);

            summary =
                $"restored:{warpName}:{safeTile.Value.X},{safeTile.Value.Y}:from:{currentLocationName}";
            ClearCapture(state);
            return true;
        }

        private static void ClearCapture(HarveyFlashbackRescueState state)
        {
            state.RescueTriggerLocation = null;
            state.RescueTriggerTileX = 0;
            state.RescueTriggerTileY = 0;
            state.PendingPositionRestore = false;
        }

        private static bool IsUnexpectedRescueExitLocation(string triggerLocation, string currentLocation)
        {
            if (!IsForestLikeLocation(triggerLocation))
                return false;

            if (IsForestLikeLocation(currentLocation))
                return false;

            return IsLocationOneOf(currentLocation, "Town", "Hospital", "HarveyRoom", "BusStop");
        }

        private static bool IsForestLikeLocation(string? locationName)
            => IsLocationOneOf(locationName, "Forest", "Woods", "SecretWoods");

        private static bool IsLocationOneOf(string? locationName, params string[] names)
        {
            if (string.IsNullOrEmpty(locationName))
                return false;

            foreach (var name in names)
            {
                if (string.Equals(locationName, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool IsSafeStandingTile(GameLocation? location, Vector2 tile)
        {
            if (location == null)
                return false;

            var x = (int)tile.X;
            var y = (int)tile.Y;
            if (!location.isTileOnMap(x, y))
                return false;

            if (!location.isTilePassable(new xTile.Dimensions.Location(x, y), Game1.viewport))
                return false;

            if (HasSitAction(location, x, y))
                return false;

            return CountWalkableNeighbors(location, x, y) >= 2;
        }

        private static bool HasSitAction(GameLocation location, int x, int y)
        {
            if (location.Map == null)
                return false;

            foreach (var layerId in new[] { "Buildings", "Back", "Front" })
            {
                Layer? layer = location.Map.GetLayer(layerId);
                if (layer == null || x < 0 || y < 0 || x >= layer.LayerWidth || y >= layer.LayerHeight)
                    continue;

                var tile = layer.Tiles[x, y];
                if (tile?.Properties.TryGetValue("Action", out var property) != true)
                    continue;

                var action = property?.ToString() ?? string.Empty;
                if (action.Contains("Sit", StringComparison.OrdinalIgnoreCase)
                    || action.Contains("Bench", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static Point? FindSafeTile(GameLocation location, Point preferred)
        {
            if (IsSafeStandingTile(location, new Vector2(preferred.X, preferred.Y)))
                return preferred;

            var visited = new HashSet<Point>();
            var queue = new Queue<Point>();
            queue.Enqueue(preferred);
            visited.Add(preferred);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var offset in CardinalOffsets)
                {
                    var next = new Point(current.X + offset.X, current.Y + offset.Y);
                    if (!visited.Add(next))
                        continue;

                    if (!location.isTileOnMap(next.X, next.Y))
                        continue;

                    if (IsSafeStandingTile(location, new Vector2(next.X, next.Y)))
                        return next;

                    if (location.isTilePassable(new xTile.Dimensions.Location(next.X, next.Y), Game1.viewport))
                        queue.Enqueue(next);
                }
            }

            if (FallbackTiles.TryGetValue(location.NameOrUniqueName ?? location.Name ?? "", out var fallback)
                && IsSafeStandingTile(location, new Vector2(fallback.X, fallback.Y)))
            {
                return fallback;
            }

            foreach (var fallbackTile in FallbackTiles.Values)
            {
                if (IsSafeStandingTile(location, new Vector2(fallbackTile.X, fallbackTile.Y)))
                    return fallbackTile;
            }

            return null;
        }

        private static int CountWalkableNeighbors(GameLocation location, int x, int y)
        {
            var count = 0;
            foreach (var offset in CardinalOffsets)
            {
                var nx = x + offset.X;
                var ny = y + offset.Y;
                if (location.isTileOnMap(nx, ny)
                    && location.isTilePassable(new xTile.Dimensions.Location(nx, ny), Game1.viewport))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool TryResolveLocation(string name, out GameLocation? location)
        {
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

            return false;
        }

        private static readonly Point[] CardinalOffsets =
        [
            new(0, 1),
            new(0, -1),
            new(1, 0),
            new(-1, 0),
        ];
    }
}
