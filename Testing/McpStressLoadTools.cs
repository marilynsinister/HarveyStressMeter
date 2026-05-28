using System.Text;
using System.Text.Json;
using System.Linq;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;

namespace HarveyStressMeter.Testing
{
    /// <summary>MCP debug tools for StressLoadService (EnableStressMcp only).</summary>
    internal static class McpStressLoadTools
    {
        public static string LoadDebug(StressLoadService stressLoadService)
            => stressLoadService.BuildMcpSnapshot();

        public static string SetLoad(StressLoadService stressLoadService, JsonElement? arguments)
        {
            if (!TryGetInt(arguments, "value", out var value))
                return "Error: value is required.";

            if (value < 0)
                return "Error: value must be >= 0.";

            var before = stressLoadService.BuildMcpSnapshot();
            stressLoadService.SetStressLoadForDebug(value);
            var after = stressLoadService.BuildMcpSnapshot();

            return BuildBeforeAfterResponse(before, after);
        }

        public static string ApplyRecovery(StressLoadService stressLoadService, JsonElement? arguments)
        {
            if (!TryGetInt(arguments, "amount", out var amount))
                return "Error: amount is required.";

            if (amount <= 0)
                return "Error: amount must be > 0.";

            TryGetString(arguments, "reason", out var reason);
            var reasonText = string.IsNullOrWhiteSpace(reason) ? "test" : reason;

            var causeLoad = stressLoadService.GetCauseLoad();
            var oldOffset = stressLoadService.GetStressRecoveryOffset();
            var oldCurrent = stressLoadService.GetCurrentStressLoad();
            var severityBefore = stressLoadService.GetSeverity();
            var activeCauseCountBefore = stressLoadService.GetActiveCauses().Count;

            stressLoadService.DebugApplyRecoveryOffset(amount, reasonText);

            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"reason: {reasonText}");
            sb.AppendLine($"CauseLoad: {causeLoad}");
            sb.AppendLine($"oldOffset: {oldOffset}");
            sb.AppendLine($"newOffset: {stressLoadService.GetStressRecoveryOffset()}");
            sb.AppendLine($"oldCurrent: {oldCurrent}");
            sb.AppendLine($"newCurrent: {stressLoadService.GetCurrentStressLoad()}");
            sb.AppendLine($"severityBefore: {severityBefore}");
            sb.AppendLine($"severityAfter: {stressLoadService.GetSeverity()}");
            sb.AppendLine($"activeCauseCountBefore: {activeCauseCountBefore}");
            sb.AppendLine($"activeCauseCountAfter: {stressLoadService.GetActiveCauses().Count}");
            return sb.ToString().TrimEnd();
        }

        public static string ClearRecoveryOffset(StressLoadService stressLoadService)
        {
            var before = stressLoadService.BuildMcpSnapshot();
            stressLoadService.DebugClearRecoveryOffset();
            var after = stressLoadService.BuildMcpSnapshot();

            return BuildBeforeAfterResponse(before, after);
        }

        public static string GotoroSetActive(StressLoadService stressLoadService, JsonElement? arguments)
        {
            if (!TryGetBool(arguments, "active", out var active))
                return "Error: active is required (boolean).";

            var syncTopics = !TryGetBool(arguments, "sync_topics", out var sync) || sync;

            stressLoadService.SetGotoroFlashbackActive(active);

            if (syncTopics)
            {
                if (active)
                {
                    if (!ConversationHelper.HasTopic(TopicIds.GotoroFlashbackActive))
                        ConversationHelper.AddTopic(TopicIds.GotoroFlashbackActive, 0);
                }
                else if (ConversationHelper.HasTopic(TopicIds.GotoroFlashbackActive))
                {
                    ConversationHelper.RemoveTopic(TopicIds.GotoroFlashbackActive);
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"GotoroFlashbackActive: {stressLoadService.IsGotoroFlashbackActive()}");
            sb.AppendLine($"CurrentStressLoad: {stressLoadService.GetCurrentStressLoad()}");
            sb.AppendLine($"Severity: {stressLoadService.GetSeverity()}");
            sb.AppendLine($"hasFlashbackTopic: {ConversationHelper.HasTopic(TopicIds.GotoroFlashbackActive)}");
            sb.AppendLine($"syncTopics: {syncTopics}");
            sb.AppendLine("ActiveCauses:");

            var causes = stressLoadService.GetActiveCauses();
            if (causes.Count == 0)
            {
                sb.AppendLine("  (none)");
            }
            else
            {
                foreach (var key in causes.Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    var cause = causes[key];
                    if (!cause.IsActive)
                        continue;

                    sb.AppendLine($"  cause: {cause.CauseId}, weight: {cause.Weight}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        public static string ForceRecalculate(StressLoadService stressLoadService)
        {
            var before = stressLoadService.BuildMcpSnapshot();
            stressLoadService.Recalculate();
            var after = stressLoadService.BuildMcpSnapshot();

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
    }
}
