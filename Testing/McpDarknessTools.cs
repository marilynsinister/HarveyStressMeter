using System.Text;
using System.Text.Json;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;

namespace HarveyStressMeter.Testing
{
    /// <summary>MCP debug tools for darkness therapy (EnableStressMcp only).</summary>
    internal static class McpDarknessTools
    {
        public static string DarknessDebug(DarknessService darknessService)
            => darknessService.BuildDebugSnapshot();

        public static string DarknessSetLevel(DarknessService darknessService, JsonElement? arguments)
        {
            if (!TryGetInt(arguments, "level", out var level))
                return "Error: level is required (1, 2, or 3).";

            var before = darknessService.BuildDebugSnapshot();
            if (!darknessService.ApplyDebugFearLevel(level))
            {
                return $"Error: invalid level '{level}' (use 1, 2, or 3).\n\n{before}";
            }

            var after = darknessService.BuildDebugSnapshot();
            return BuildBeforeAfterResponse(before, after);
        }

        public static string DarknessStartTherapy(DarknessService darknessService)
        {
            var before = darknessService.BuildDebugSnapshot();
            darknessService.StartTherapy();
            var after = darknessService.BuildDebugSnapshot();
            return BuildBeforeAfterResponse(before, after);
        }

        public static string DarknessStep1Progress(DarknessService darknessService, JsonElement? arguments)
        {
            if (TryGetInt(arguments, "evenings", out var evenings)
                && TryGetInt(arguments, "today", out var today))
            {
                var before = darknessService.BuildDebugSnapshot();
                if (!darknessService.ApplyDebugStep1Progress(evenings, today))
                {
                    return $"Error: step1 progress failed (start therapy at stage 1 first).\n\n{before}";
                }

                var after = darknessService.BuildDebugSnapshot();
                var sb = new StringBuilder();
                sb.AppendLine($"evenings: {evenings}");
                sb.AppendLine($"today: {today}");
                sb.Append(BuildBeforeAfterResponse(before, after));
                return sb.ToString().TrimEnd();
            }

            if (TryGetInt(arguments, "value", out var legacyValue))
            {
                var useLegacy = TryGetBool(arguments, "legacy", out var legacy) && legacy;
                var before = darknessService.BuildDebugSnapshot();
                if (!useLegacy)
                {
                    if (!darknessService.ApplyDebugStep1Progress(legacyValue, 0))
                        return $"Error: step1 progress failed (start therapy at stage 1 first).\n\n{before}";
                }
                else if (!darknessService.ApplyDebugStep1ProgressLegacy(legacyValue))
                {
                    return $"Error: step1 progress failed (start therapy at stage 1 first).\n\n{before}";
                }

                var after = darknessService.BuildDebugSnapshot();
                var sb = new StringBuilder();
                sb.AppendLine($"mode: {(useLegacy ? "legacy SafeDarknessMinutes" : "evenings only")}");
                sb.AppendLine($"value: {legacyValue}");
                sb.Append(BuildBeforeAfterResponse(before, after));
                return sb.ToString().TrimEnd();
            }

            return "Error: evenings and today are required (or legacy value).";
        }

        public static string DarknessSync(DarknessService darknessService)
        {
            var before = darknessService.BuildDebugSnapshot();
            darknessService.SyncDarknessState("mcp stress_darkness_sync");
            var after = darknessService.BuildDebugSnapshot();
            return BuildBeforeAfterResponse(before, after);
        }

        public static string TreatmentDebug(
            SaveData data,
            StateService stateService,
            StressLoadService stressLoadService,
            TreatmentEpisodeService episodeService,
            StressDialogueService stressDialogueService)
            => TreatmentDebugReporter.BuildTreatmentDebugSnapshot(
                data,
                stateService,
                stressLoadService,
                episodeService,
                stressDialogueService);

        private static string BuildBeforeAfterResponse(string before, string after)
        {
            var sb = new StringBuilder();
            sb.AppendLine("--- before ---");
            sb.AppendLine(before);
            sb.AppendLine("--- after ---");
            sb.AppendLine(after);
            return sb.ToString().TrimEnd();
        }

        private static bool TryGetInt(JsonElement? arguments, string name, out int value)
        {
            value = 0;
            if (arguments?.ValueKind != JsonValueKind.Object)
                return false;

            if (!arguments.Value.TryGetProperty(name, out var prop))
                return false;

            return prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out value);
        }

        private static bool TryGetBool(JsonElement? arguments, string name, out bool value)
        {
            value = false;
            if (arguments?.ValueKind != JsonValueKind.Object)
                return false;

            if (!arguments.Value.TryGetProperty(name, out var prop))
                return false;

            if (prop.ValueKind == JsonValueKind.True)
            {
                value = true;
                return true;
            }

            if (prop.ValueKind == JsonValueKind.False)
                return true;

            return false;
        }
    }
}
