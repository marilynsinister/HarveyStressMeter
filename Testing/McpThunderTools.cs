using System.Text;
using System.Text.Json;
using HarveyStressMeter.Services;

namespace HarveyStressMeter.Testing
{
    /// <summary>MCP debug tools for thunder stabilization / relapse (EnableStressMcp only).</summary>
    internal static class McpThunderTools
    {
        public static string ThunderDebug(ThunderFlashbackService thunderService, StressLoadService stressLoadService)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.Append(thunderService.BuildMcpSnapshot());
            sb.AppendLine();
            sb.AppendLine("=== Active causes ===");
            foreach (var (causeId, cause) in stressLoadService.GetActiveCauses())
            {
                sb.AppendLine($"  {causeId}: weight={cause.Weight}, active={cause.IsActive}, severe={cause.IsSevere}");
            }

            return sb.ToString().TrimEnd();
        }

        public static string ThunderForceRelapse(ThunderFlashbackService thunderService, JsonElement? arguments)
        {
            var ignoreChance = !TryGetBool(arguments, "ignore_chance", out var ignore) || ignore;
            var applied = thunderService.TryRollThunderRelapse(ignoreChance: ignoreChance);

            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"applied: {applied}");
            sb.AppendLine();
            sb.Append(thunderService.BuildMcpSnapshot());
            return sb.ToString().TrimEnd();
        }

        public static string ThunderStabilizeHarvey(
            ThunderFlashbackService thunderService,
            JsonElement? arguments)
        {
            var decay = TryGetInt(arguments, "stress_decay", out var decayValue) ? decayValue : 15;
            var grace = TryGetInt(arguments, "grace_minutes", out var graceValue)
                ? graceValue
                : 120;
            var reason = TryGetString(arguments, "reason", out var reasonValue)
                ? reasonValue
                : "mcp_force";

            thunderService.StabilizeWithHarvey(decay, grace, reason);

            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"reason: {reason}");
            sb.AppendLine();
            sb.Append(thunderService.BuildMcpSnapshot());
            return sb.ToString().TrimEnd();
        }

        public static string ThunderClear(ThunderFlashbackService thunderService)
        {
            thunderService.ResetFlashbackState();

            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine("cleared: thunder/relapse/sensitivity state");
            sb.AppendLine();
            sb.Append(thunderService.BuildMcpSnapshot());
            return sb.ToString().TrimEnd();
        }

        private static bool TryGetBool(JsonElement? arguments, string name, out bool value)
        {
            value = false;
            if (arguments?.TryGetProperty(name, out var element) != true)
                return false;

            if (element.ValueKind == JsonValueKind.True)
            {
                value = true;
                return true;
            }

            if (element.ValueKind == JsonValueKind.False)
                return true;

            return bool.TryParse(element.GetString(), out value);
        }

        private static bool TryGetInt(JsonElement? arguments, string name, out int value)
        {
            value = 0;
            if (arguments?.TryGetProperty(name, out var element) != true)
                return false;

            if (element.TryGetInt32(out value))
                return true;

            return int.TryParse(element.GetString(), out value);
        }

        private static bool TryGetString(JsonElement? arguments, string name, out string value)
        {
            value = "";
            if (arguments?.TryGetProperty(name, out var element) != true)
                return false;

            value = element.GetString() ?? "";
            return !string.IsNullOrWhiteSpace(value);
        }
    }
}
