using System;
using System.Reflection;
using HarveyStressMeter.Helpers;
using Microsoft.Xna.Framework;
using StardewValley;

namespace HarveyStressMeter.Testing
{
    /// <summary>
    /// MCP debug warps: instant completion for in-plan steps, fade-settle checks for deferred mcp_warp.
    /// Game1.warpFarmer starts a fade; without completing the fade callback the player stays in the old location.
    /// </summary>
    internal static class McpWarpHelper
    {
        private const int DefaultFacing = 2;
        private const int WarpSettleTimeoutMs = 12_000;

        private static readonly MethodInfo? PerformWarpFarmerMethod =
            typeof(Game1).GetMethod(
                "performWarpFarmer",
                BindingFlags.Static | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(LocationRequest), typeof(int), typeof(int), typeof(int) },
                modifiers: null);

        private static readonly MethodInfo? OnFadeToBlackCompleteMethod =
            typeof(Game1).GetMethod(
                "onFadeToBlackComplete",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);

        public static bool IsWarpSettled()
            => !Game1.isWarping
               && !Game1.fadeToBlack
               && !Game1.globalFade
               && Game1.locationRequest == null;

        public static bool IsPlayerAtLocation(string locationName, int? x = null, int? y = null)
        {
            var current = GetLocationName();
            if (!LocationNamesMatch(current, locationName))
                return false;

            if (!x.HasValue || !y.HasValue)
                return true;

            var tile = Game1.player.Tile;
            return (int)tile.X == x.Value && (int)tile.Y == y.Value;
        }

        /// <summary>Instant debug warp for test-plan steps that must finish on the same game tick.</summary>
        public static bool TryWarpImmediate(
            string locationName,
            int x,
            int y,
            out string? error,
            int facingDirection = DefaultFacing)
        {
            return TryWarpImmediate(locationName, x, y, out _, out _, out error, facingDirection);
        }

        public static bool TryWarpImmediate(
            string locationName,
            int x,
            int y,
            out int resolvedX,
            out int resolvedY,
            out string? error,
            int facingDirection = DefaultFacing)
        {
            resolvedX = x;
            resolvedY = y;
            error = null;

            ResetPlayerMovementState();

            if (!McpEnvironmentTools.TryResolveLocation(locationName, out var location, out var resolveError))
            {
                error = resolveError.StartsWith("Error:", StringComparison.Ordinal)
                    ? resolveError
                    : $"Error: {resolveError}";
                return false;
            }

            var warpName = location!.NameOrUniqueName ?? location.Name ?? locationName;

            if (!RescuePlayerPositionHelper.TryFindSafeStandingTile(location, new Point(x, y), out var safeTile))
            {
                error = $"Error: no safe standing tile near {warpName} ({x},{y}).";
                return false;
            }

            resolvedX = safeTile.X;
            resolvedY = safeTile.Y;

            if (IsPlayerAtLocation(warpName, resolvedX, resolvedY)
                && IsWarpSettled()
                && !Game1.player.IsSitting())
            {
                ResetPlayerMovementState();
                return true;
            }

            try
            {
                var request = Game1.getLocationRequest(warpName);
                if (request == null)
                {
                    error = $"Error: location request for '{warpName}' is null.";
                    return false;
                }

                if (PerformWarpFarmerMethod == null || OnFadeToBlackCompleteMethod == null)
                {
                    error = "Error: warp reflection hooks unavailable (game API changed).";
                    return false;
                }

                PerformWarpFarmerMethod.Invoke(null, new object[] { request, resolvedX, resolvedY, facingDirection });

                if (Game1.game1 != null)
                    OnFadeToBlackCompleteMethod.Invoke(Game1.game1, null);

                ForceClearWarpFade();

                if (!IsPlayerAtLocation(warpName, resolvedX, resolvedY))
                {
                    error =
                        $"Error: warp verification failed (still at {GetLocationName()} " +
                        $"({(int)Game1.player.Tile.X},{(int)Game1.player.Tile.Y}), expected {warpName} {resolvedX},{resolvedY}).";
                    return false;
                }

                if (Game1.player.IsSitting())
                {
                    error = $"Error: warp left player sitting at {warpName} ({resolvedX},{resolvedY}).";
                    return false;
                }

                if (!IsWarpSettled())
                {
                    ForceClearWarpFade();
                    if (!IsWarpSettled()
                        && IsPlayerAtLocation(warpName, resolvedX, resolvedY)
                        && !Game1.player.IsSitting())
                    {
                        // Destination reached; stale fade flags must not fail MCP test warps.
                        ForceClearWarpFade();
                        return true;
                    }

                    if (!IsWarpSettled())
                    {
                        error =
                            $"Error: warp verification failed (fade/warp state still active: " +
                            $"isWarping={Game1.isWarping}, fadeToBlack={Game1.fadeToBlack}, globalFade={Game1.globalFade}).";
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"Error: instant warp failed: {ex.Message}";
                return false;
            }
        }

        public static bool TryResolveSafeTile(
            GameLocation location,
            int x,
            int y,
            out int resolvedX,
            out int resolvedY,
            out string? error)
        {
            resolvedX = x;
            resolvedY = y;
            error = null;

            if (RescuePlayerPositionHelper.TryFindSafeStandingTile(location, new Point(x, y), out var safeTile))
            {
                resolvedX = safeTile.X;
                resolvedY = safeTile.Y;
                return true;
            }

            error = $"no safe standing tile near ({x},{y})";
            return false;
        }

        /// <summary>Clears fade/warp flags after instant MCP warps (performWarpFarmer can leave fadeToBlack set).</summary>
        public static void ForceClearWarpFade()
        {
            GameStateHelper.ForceClearFadeAndWarpFlags();
            ResetPlayerMovementState();
            GameStateHelper.ClearStaleUiFlags();
        }

        public static void ResetPlayerMovementState()
        {
            if (Game1.player.IsSitting())
                Game1.player.StopSitting(animate: false);

            if (Game1.player.UsingTool)
                Game1.player.completelyStopAnimatingOrDoingAction();

            Game1.player.Halt();
            Game1.player.forceCanMove();
            Game1.player.ignoreCollisions = false;
        }

        /// <summary>Starts a normal fade warp; completion is polled by <see cref="McpWarpService"/>.</summary>
        public static bool TryBeginFadeWarp(
            string locationName,
            int x,
            int y,
            out string? error,
            out int resolvedX,
            out int resolvedY,
            int facingDirection = DefaultFacing)
        {
            resolvedX = x;
            resolvedY = y;
            error = null;

            ResetPlayerMovementState();

            if (!McpEnvironmentTools.TryResolveLocation(locationName, out var location, out var resolveError))
            {
                error = resolveError.StartsWith("Error:", StringComparison.Ordinal)
                    ? resolveError
                    : $"Error: {resolveError}";
                return false;
            }

            if (!RescuePlayerPositionHelper.TryFindSafeStandingTile(location!, new Point(x, y), out var safeTile))
            {
                error = $"Error: no safe standing tile near {locationName} ({x},{y}).";
                return false;
            }

            resolvedX = safeTile.X;
            resolvedY = safeTile.Y;
            var warpName = location!.NameOrUniqueName ?? location.Name ?? locationName;

            if (IsPlayerAtLocation(warpName, resolvedX, resolvedY) && IsWarpSettled() && !Game1.player.IsSitting())
                return true;

            Game1.warpFarmer(warpName, resolvedX, resolvedY, facingDirection);

            if (IsPlayerAtLocation(warpName, resolvedX, resolvedY) && IsWarpSettled() && !Game1.player.IsSitting())
            {
                ResetPlayerMovementState();
                return true;
            }

            return false;
        }

        public static bool IsPendingWarpComplete(string locationName, int x, int y, DateTime startedUtc)
        {
            if ((DateTime.UtcNow - startedUtc).TotalMilliseconds > WarpSettleTimeoutMs)
                return false;

            return IsPlayerAtLocation(locationName, x, y)
                   && IsWarpSettled()
                   && !Game1.player.IsSitting();
        }

        public static bool IsPendingWarpTimedOut(DateTime startedUtc)
            => (DateTime.UtcNow - startedUtc).TotalMilliseconds > WarpSettleTimeoutMs;

        internal static string GetLocationName()
            => Game1.currentLocation?.NameOrUniqueName ?? Game1.currentLocation?.Name ?? "null";

        internal static bool LocationNamesMatch(string? actual, string? expected)
        {
            if (string.IsNullOrWhiteSpace(actual) || string.IsNullOrWhiteSpace(expected))
                return false;

            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }
    }
}

