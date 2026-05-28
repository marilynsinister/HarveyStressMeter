using System.Text;
using System.Text.Json;
using HarveyStressMeter.Helpers;
using StardewModdingAPI;

namespace HarveyStressMeter.Testing
{
    /// <summary>MCP tools for CP event testing (EnableStressMcp only).</summary>
    internal static class McpEventTools
    {
        public static string EventSnapshot()
            => EventDebugHelper.BuildMcpSnapshot();

        public static string StartEvent(IMonitor monitor, JsonElement? arguments)
        {
            if (!TryGetString(arguments, "event_id", out var eventId))
                return "Error: event_id is required.";

            var force = TryGetBool(arguments, "force", out var forceValue) && forceValue;
            string? locationArg = null;
            if (TryGetString(arguments, "location", out var location))
                locationArg = location.Trim();

            if (EventDebugHelper.GetStartBlockReason(force) is { } blockReason)
            {
                return FormatBlockedStart(blockReason);
            }

            var sb = new StringBuilder();
            sb.AppendLine("[before]");
            sb.AppendLine(EventDebugHelper.BuildMcpSnapshot());

            if (!string.IsNullOrWhiteSpace(locationArg))
            {
                var warpResult = McpEnvironmentTools.WarpToLocation(locationArg);
                sb.AppendLine("[warp]");
                sb.AppendLine(warpResult);

                if (warpResult.StartsWith("Error:", System.StringComparison.Ordinal))
                {
                    sb.AppendLine("[after]");
                    sb.AppendLine(EventDebugHelper.BuildMcpSnapshot());
                    return sb.ToString().TrimEnd();
                }
            }

            var result = EventDebugHelper.TryStartEvent(eventId, monitor);
            sb.AppendLine("ok: true");
            sb.AppendLine($"started: {result.Started}");
            sb.AppendLine($"usedCpScript: {result.UsedCpScript}");
            sb.AppendLine($"unsupported: {(result.UnsupportedReason ?? "(none)")}");

            if (result.Started)
            {
                sb.AppendLine(
                    "warning: event started via debug MCP — stress dialogue tools should return Error until event ends.");
            }

            sb.AppendLine("[after]");
            sb.AppendLine(result.AfterSnapshot);
            return sb.ToString().TrimEnd();
        }

        public static string EndEvent(JsonElement? arguments)
        {
            var force = TryGetBool(arguments, "force", out var forceValue) && forceValue;
            var result = EventDebugHelper.TryEndEvent(force);

            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"ended: {result.Ended}");
            sb.AppendLine($"unsupported: {(result.UnsupportedReason ?? "(none)")}");
            sb.AppendLine("[before]");
            sb.AppendLine(result.BeforeSnapshot);
            sb.AppendLine("[after]");
            sb.AppendLine(result.AfterSnapshot);
            return sb.ToString().TrimEnd();
        }

        public static string AdvanceEvent(JsonElement? arguments)
        {
            var steps = 1;
            if (TryGetInt(arguments, "steps", out var parsed) && parsed > 0)
                steps = System.Math.Min(parsed, 50);

            var force = TryGetBool(arguments, "force", out var forceValue) && forceValue;
            var result = EventDebugHelper.TryAdvanceEvent(steps, force);

            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"advanced: {result.Advanced}");
            sb.AppendLine($"requestedSteps: {result.RequestedSteps}");
            sb.AppendLine($"advancedSteps: {result.AdvancedSteps}");
            sb.AppendLine($"unsupported: {(result.UnsupportedReason ?? "(none)")}");

            if (!string.IsNullOrEmpty(result.Warning))
                sb.AppendLine($"warning: {result.Warning}");

            if (result.StuckAtCommandIndex >= 0)
                sb.AppendLine($"stuckAtCommandIndex: {result.StuckAtCommandIndex}");

            sb.AppendLine("[before]");
            sb.AppendLine(result.BeforeSnapshot);
            sb.AppendLine("[after]");
            sb.AppendLine(result.AfterSnapshot);
            return sb.ToString().TrimEnd();
        }

        private static string FormatBlockedStart(string blockReason)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Error: {blockReason}. mcp_start_event blocked.");
            sb.AppendLine("hint: close menu/dialogue or pass force=true (does not auto-close UI).");
            sb.AppendLine();
            sb.Append(EventDebugHelper.BuildMcpSnapshot());
            return sb.ToString().TrimEnd();
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

        private static bool TryGetBool(JsonElement? arguments, string name, out bool value)
        {
            value = false;
            if (arguments is not { ValueKind: JsonValueKind.Object } args
                || !args.TryGetProperty(name, out var el))
            {
                return false;
            }

            if (el.ValueKind == JsonValueKind.True) { value = true; return true; }
            if (el.ValueKind == JsonValueKind.False) { value = false; return true; }
            return false;
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
