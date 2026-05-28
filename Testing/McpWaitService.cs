using System;
using System.Text;
using System.Text.Json;
using StardewModdingAPI;
using StardewValley;

namespace HarveyStressMeter.Testing
{
    /// <summary>Tick-based real-time wait for mcp_wait_seconds (does not block the game thread).</summary>
    internal sealed class McpWaitService
    {
        private PendingWait? _pending;

        private sealed class PendingWait
        {
            public TaskCompletionSource<string> Completion { get; init; } = null!;
            public DateTime StartedUtc { get; init; }
            public int RequestedSeconds { get; init; }
            public int GameTimeBefore { get; init; }
            public string Location { get; init; } = "";
        }

        public bool HasPending => _pending != null;

        /// <summary>
        /// Handles mcp_wait_seconds. Returns true if this tool was handled (deferred or completed inline).
        /// </summary>
        public bool TryHandle(string toolName, JsonElement? arguments, TaskCompletionSource<string> completion)
        {
            if (!string.Equals(toolName, "mcp_wait_seconds", StringComparison.Ordinal))
                return false;

            if (!Context.IsWorldReady)
            {
                completion.TrySetResult("Error: load a save first.");
                return true;
            }

            if (!TryGetInt(arguments, "seconds", out var seconds) || seconds < 0)
            {
                completion.TrySetResult("Error: seconds is required and must be >= 0.");
                return true;
            }

            if (_pending != null)
            {
                completion.TrySetResult("Error: another mcp_wait_seconds is already in progress.");
                return true;
            }

            var gameTimeBefore = Game1.timeOfDay;
            var location = Game1.currentLocation?.NameOrUniqueName ?? Game1.currentLocation?.Name ?? "null";

            if (seconds == 0)
            {
                completion.TrySetResult(BuildResult(0, 0, gameTimeBefore, gameTimeBefore, location));
                return true;
            }

            _pending = new PendingWait
            {
                Completion = completion,
                StartedUtc = DateTime.UtcNow,
                RequestedSeconds = seconds,
                GameTimeBefore = gameTimeBefore,
                Location = location,
            };

            return true;
        }

        public void Tick()
        {
            if (_pending == null)
                return;

            var elapsed = (int)Math.Floor((DateTime.UtcNow - _pending.StartedUtc).TotalSeconds);
            if (elapsed < _pending.RequestedSeconds)
                return;

            var completed = Math.Min(elapsed, _pending.RequestedSeconds);
            var result = BuildResult(
                _pending.RequestedSeconds,
                completed,
                _pending.GameTimeBefore,
                Game1.timeOfDay,
                Game1.currentLocation?.NameOrUniqueName ?? Game1.currentLocation?.Name ?? "null");

            _pending.Completion.TrySetResult(result);
            _pending = null;
        }

        private static string BuildResult(
            int requestedSeconds,
            int completedSeconds,
            int gameTimeBefore,
            int gameTimeAfter,
            string location)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"requestedSeconds: {requestedSeconds}");
            sb.AppendLine($"completedSeconds: {completedSeconds}");
            sb.AppendLine($"gameTimeBefore: {gameTimeBefore}");
            sb.AppendLine($"gameTimeAfter: {gameTimeAfter}");
            sb.AppendLine($"location: {location}");
            sb.AppendLine("warning: wait uses real-time ticks; game time may advance independently during long waits.");
            return sb.ToString().TrimEnd();
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
