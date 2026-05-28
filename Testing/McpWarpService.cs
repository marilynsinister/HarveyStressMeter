using System;
using System.Text;
using System.Text.Json;
using StardewModdingAPI;
using StardewValley;

namespace HarveyStressMeter.Testing
{
    /// <summary>Deferred completion for mcp_warp while screen fade and location transition settle.</summary>
    internal sealed class McpWarpService
    {
        private PendingWarp? _pending;

        public bool HasPending => _pending != null;

        public void Tick()
        {
            if (_pending == null)
                return;

            if (McpWarpHelper.IsPendingWarpComplete(_pending.LocationName, _pending.X, _pending.Y, _pending.StartedUtc))
            {
                McpWarpHelper.ResetPlayerMovementState();
                Complete(BuildSuccessResult(_pending));
                return;
            }

            if (McpWarpHelper.IsPendingWarpTimedOut(_pending.StartedUtc))
            {
                Complete(
                    $"Error: warp timeout after {McpWarpHelper.GetLocationName()} " +
                    $"(expected {_pending.LocationName} {_pending.X},{_pending.Y}; " +
                    $"isWarping={Game1.isWarping}, fadeToBlack={Game1.fadeToBlack}, globalFade={Game1.globalFade}).\n\n" +
                    Helpers.TestContextReporter.BuildReport());
            }
        }

        public bool TryHandle(string toolName, JsonElement? arguments, TaskCompletionSource<string> completion)
        {
            if (!string.Equals(toolName, "mcp_warp", StringComparison.Ordinal))
                return false;

            if (_pending != null)
            {
                completion.TrySetResult("Error: another mcp_warp is already in progress.");
                return true;
            }

            if (!Context.IsWorldReady)
            {
                completion.TrySetResult("Error: load a save first.");
                return true;
            }

            if (McpEnvironmentTools.EnvironmentBlockMessage("mcp_warp") is { } blocked)
            {
                completion.TrySetResult(blocked);
                return true;
            }

            if (!TryParseWarpArguments(arguments, out var locationName, out var x, out var y, out var parseError))
            {
                completion.TrySetResult(parseError);
                return true;
            }

            var oldLocation = McpWarpHelper.GetLocationName();

            if (McpWarpHelper.TryBeginFadeWarp(locationName, x, y, out var beginError, out var resolvedX, out var resolvedY))
            {
                completion.TrySetResult(BuildSuccessResult(oldLocation, locationName, x, y, resolvedX, resolvedY, waitedMs: 0));
                return true;
            }

            if (beginError != null)
            {
                completion.TrySetResult(beginError);
                return true;
            }

            var warpName = locationName;
            if (McpEnvironmentTools.TryResolveLocation(locationName, out var location, out _))
                warpName = location!.NameOrUniqueName ?? location.Name ?? locationName;

            _pending = new PendingWarp
            {
                Completion = completion,
                StartedUtc = DateTime.UtcNow,
                OldLocation = oldLocation,
                LocationName = warpName,
                RequestedX = x,
                RequestedY = y,
                X = resolvedX,
                Y = resolvedY,
            };

            return true;
        }

        private void Complete(string result)
        {
            var pending = _pending;
            _pending = null;
            pending?.Completion.TrySetResult(result);
        }

        private static string BuildSuccessResult(PendingWarp pending)
        {
            var waitedMs = (int)Math.Round((DateTime.UtcNow - pending.StartedUtc).TotalMilliseconds);
            return BuildSuccessResult(
                pending.OldLocation,
                pending.LocationName,
                pending.RequestedX,
                pending.RequestedY,
                pending.X,
                pending.Y,
                waitedMs);
        }

        private static string BuildSuccessResult(
            string oldLocation,
            string locationName,
            int requestedX,
            int requestedY,
            int resolvedX,
            int resolvedY,
            int waitedMs)
        {
            var warpName = locationName;
            if (McpEnvironmentTools.TryResolveLocation(locationName, out var location, out _))
                warpName = location!.NameOrUniqueName ?? location.Name ?? locationName;

            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"oldLocation: {oldLocation}");
            sb.AppendLine($"newLocation: {McpWarpHelper.GetLocationName()}");
            sb.AppendLine($"tile: {resolvedX},{resolvedY}");
            if (resolvedX != requestedX || resolvedY != requestedY)
                sb.AppendLine($"requestedTile: {requestedX},{requestedY}");
            sb.AppendLine($"warpTarget: {warpName}");
            sb.AppendLine($"fadeWaitMs: {waitedMs}");
            sb.AppendLine($"isWarping: {Game1.isWarping}");
            sb.AppendLine($"fadeToBlack: {Game1.fadeToBlack}");
            sb.AppendLine($"isSitting: {Game1.player.IsSitting()}");

            if (Helpers.GameStateHelper.IsEventActive())
            {
                sb.AppendLine(
                    $"warning: event active after warp (EventId={Helpers.GameStateHelper.GetCurrentEventId() ?? "unknown"}).");
            }

            if (!McpWarpHelper.IsPlayerAtLocation(warpName, resolvedX, resolvedY))
            {
                sb.AppendLine(
                    $"warning: location verification mismatch (current={McpWarpHelper.GetLocationName()}, " +
                    $"tile={(int)Game1.player.Tile.X},{(int)Game1.player.Tile.Y}).");
            }

            return sb.ToString().TrimEnd();
        }

        private static bool TryParseWarpArguments(
            JsonElement? arguments,
            out string locationName,
            out int x,
            out int y,
            out string error)
        {
            locationName = string.Empty;
            x = 0;
            y = 0;
            error = string.Empty;

            if (arguments is not { ValueKind: JsonValueKind.Object } args
                || !args.TryGetProperty("location", out var locEl)
                || locEl.ValueKind != JsonValueKind.String)
            {
                error = "Error: location is required.";
                return false;
            }

            locationName = locEl.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(locationName))
            {
                error = "Error: location is required.";
                return false;
            }

            if (args.TryGetProperty("x", out var xEl) && xEl.TryGetInt32(out var argX)
                && args.TryGetProperty("y", out var yEl) && yEl.TryGetInt32(out var argY))
            {
                x = argX;
                y = argY;
                return true;
            }

            if (McpEnvironmentTools.TryGetDefaultTile(locationName, out var tile))
            {
                x = tile.X;
                y = tile.Y;
                return true;
            }

            x = 5;
            y = 5;
            return true;
        }

        private sealed class PendingWarp
        {
            public TaskCompletionSource<string> Completion { get; init; } = null!;
            public DateTime StartedUtc { get; init; }
            public string OldLocation { get; init; } = "";
            public string LocationName { get; init; } = "";
            public int RequestedX { get; init; }
            public int RequestedY { get; init; }
            public int X { get; init; }
            public int Y { get; init; }
        }
    }
}
