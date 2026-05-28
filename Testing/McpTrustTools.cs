using System.Text;
using System.Text.Json;
using HarveyStressMeter.Services;

namespace HarveyStressMeter.Testing
{
    /// <summary>MCP debug tools for HarveyCareTrustService (EnableStressMcp only).</summary>
    internal static class McpTrustTools
    {
        public static string TrustDebug(HarveyCareTrustService trustService)
            => trustService.BuildMcpSnapshot();

        public static string TrustSet(HarveyCareTrustService trustService, JsonElement? arguments)
        {
            if (!TryGetInt(arguments, "points", out var points))
                return "Error: points is required.";

            if (points < 0)
                return "Error: points must be >= 0.";

            var before = trustService.BuildMcpSnapshot();
            trustService.SetTrustPointsForDebug(points);
            var after = trustService.BuildMcpSnapshot();

            return BuildBeforeAfterResponse(before, after);
        }

        public static string TrustAdd(HarveyCareTrustService trustService, JsonElement? arguments)
        {
            if (!TryGetInt(arguments, "points", out var points))
                return "Error: points is required.";

            if (points <= 0)
                return "Error: points must be > 0.";

            TryGetString(arguments, "reason", out var reason);

            var before = trustService.BuildMcpSnapshot();
            trustService.AddTrustPointsForDebug(points, string.IsNullOrWhiteSpace(reason) ? "mcp" : reason);
            var after = trustService.BuildMcpSnapshot();

            var sb = new StringBuilder();
            sb.AppendLine($"reason: {(string.IsNullOrWhiteSpace(reason) ? "mcp" : reason)}");
            sb.AppendLine($"pointsAdded: {points}");
            sb.Append(BuildBeforeAfterResponse(before, after));
            return sb.ToString().TrimEnd();
        }

        public static string TrustRemove(HarveyCareTrustService trustService, JsonElement? arguments)
        {
            if (!TryGetInt(arguments, "points", out var points))
                return "Error: points is required.";

            if (points <= 0)
                return "Error: points must be > 0.";

            TryGetString(arguments, "reason", out var reason);

            var before = trustService.BuildMcpSnapshot();
            trustService.RemoveTrustPointsForDebug(points, string.IsNullOrWhiteSpace(reason) ? "mcp" : reason);
            var after = trustService.BuildMcpSnapshot();

            var sb = new StringBuilder();
            sb.AppendLine($"reason: {(string.IsNullOrWhiteSpace(reason) ? "mcp" : reason)}");
            sb.AppendLine($"pointsRemoved: {points}");
            sb.Append(BuildBeforeAfterResponse(before, after));
            return sb.ToString().TrimEnd();
        }

        public static string TrustReset(HarveyCareTrustService trustService)
        {
            var before = trustService.BuildMcpSnapshot();
            trustService.ResetTrustState();
            var after = trustService.BuildMcpSnapshot();

            return BuildBeforeAfterResponse(before, after);
        }

        private static string BuildBeforeAfterResponse(string before, string after)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[before]");
            sb.AppendLine(before);
            sb.AppendLine("[after]");
            sb.Append(after);
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
            return true;
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
