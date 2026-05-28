using System.Text;
using System.Text.Json;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Services;

namespace HarveyStressMeter.Testing
{
    /// <summary>MCP debug tools for Gotoro forest rescue (EnableStressMcp only).</summary>
    internal static class McpRescueTools
    {
        public static string RescueDebug(HarveyFlashbackRescueService rescueService)
            => rescueService.BuildMcpSnapshot();

        public static string RescueEvaluate(HarveyFlashbackRescueService rescueService, JsonElement? arguments)
        {
            var ignoreChance = TryGetBool(arguments, "ignore_chance", out var ignore) && ignore;
            var rollEval = rescueService.EvaluateRescueWithRoll(ignoreChance);
            var eval = rollEval.Evaluation;

            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"CanAttempt: {eval.CanAttempt}");
            sb.AppendLine($"RollPerformed: {rollEval.RollPerformed}");
            sb.AppendLine($"RollValue: {FormatNullableDouble(rollEval.RollValue)}");
            sb.AppendLine($"RescueChance: {eval.RescueChance:0.###}");
            sb.AppendLine($"PassedRoll: {FormatNullableBool(rollEval.PassedRoll)}");
            sb.AppendLine($"PendingTopicAdded: {rollEval.PendingTopicAdded}");
            sb.AppendLine($"BlockReason: {eval.BlockReason ?? "(none)"}");
            sb.AppendLine($"CandidateTier: {eval.Tier ?? "(none)"}");
            sb.AppendLine($"BaseChance: {eval.BaseRescueChance:0.###}");
            sb.AppendLine($"TrustBonus: {eval.TrustRescueBonus:0.###}");
            sb.AppendLine($"FinalChance: {eval.RescueChance:0.###}");
            sb.AppendLine("DebugSnapshot:");
            sb.Append(rescueService.BuildMcpSnapshot());
            return sb.ToString().TrimEnd();
        }

        public static string RescueForce(HarveyFlashbackRescueService rescueService, JsonElement? arguments)
        {
            if (!TryGetBool(arguments, "force", out var force))
                force = true;

            string? tierArg = null;
            if (TryGetString(arguments, "tier", out var tier))
                tierArg = tier.Trim();

            if (!string.IsNullOrWhiteSpace(tierArg)
                && !string.Equals(tierArg, "auto", StringComparison.OrdinalIgnoreCase)
                && !FlashbackRescueTiers.TryParse(tierArg, out _))
            {
                return "Error: tier must be auto, MidTrust, HighTrust, Dating, or Married.";
            }

            var result = rescueService.PrepareRescueDebug(tierArg, force);

            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"force: {force}");
            sb.AppendLine($"tier: {result.Tier ?? "(none)"}");
            sb.AppendLine($"naturalTier: {result.NaturalTier ?? "(none)"}");
            sb.AppendLine($"eventId: {(result.Tier != null ? FlashbackRescueEventIds.ForTier(result.Tier) : "(none)")}");
            sb.AppendLine($"CanAttempt: {result.CanAttempt}");
            sb.AppendLine($"BlockReason: {result.BlockReason ?? "(none)"}");
            sb.AppendLine($"topicsFlashbackBefore: {result.TopicsBeforeFlashback}");
            sb.AppendLine($"topicsFlashbackAfter: {result.TopicsAfterFlashback}");
            sb.AppendLine($"topicsPendingBefore: {result.TopicsBeforePending}");
            sb.AppendLine($"topicsPendingAfter: {result.TopicsAfterPending}");
            sb.AppendLine($"location: {result.LocationName ?? "null"}");
            sb.AppendLine($"isForestLikeLocation: {result.IsForestLikeLocation}");
            sb.AppendLine($"weather: {result.Weather ?? "unknown"}");
            sb.AppendLine($"isStorm: {result.IsStorm}");
            sb.AppendLine($"hearts: {result.Hearts}");
            sb.AppendLine($"friendshipPoints: {result.FriendshipPoints}");
            sb.AppendLine($"relationshipStatus: {result.RelationshipStatus ?? "none"}");

            if (!string.IsNullOrEmpty(result.Warning))
                sb.AppendLine($"warning: {result.Warning}");

            if (force)
            {
                sb.AppendLine(
                    "warning: debug prepare only — CP event not started; enter Forest/Woods with storm + topics to trigger.");
            }

            if (!result.IsForestLikeLocation)
            {
                sb.AppendLine(
                    "warning: player not in Forest/Woods/SecretWoods — warp with mcp_warp before expecting CP event.");
            }

            if (!result.IsStorm)
            {
                sb.AppendLine(
                    "warning: not storm weather — use mcp_set_weather { \"weather\": \"storm\" } for rescue conditions.");
            }

            return sb.ToString().TrimEnd();
        }

        public static string RescueClear(HarveyFlashbackRescueService rescueService)
        {
            var before = rescueService.BuildMcpSnapshot();
            rescueService.ClearRescuePendingForMcp();
            var after = rescueService.BuildMcpSnapshot();

            var sb = new StringBuilder();
            sb.AppendLine("[before]");
            sb.AppendLine(before);
            sb.AppendLine("[after]");
            sb.Append(after);
            return sb.ToString().TrimEnd();
        }

        private static string FormatNullableDouble(double? value)
            => value.HasValue ? value.Value.ToString("0.###") : "(none)";

        private static string FormatNullableBool(bool? value)
            => value.HasValue ? value.Value.ToString().ToLowerInvariant() : "(none)";

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
