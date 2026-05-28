using System.Text;
using HarveyStressMeter.Services;

namespace HarveyStressMeter.Testing
{
    /// <summary>MCP debug tools for Harvey Safe Person Aura (EnableStressMcp only).</summary>
    internal static class McpSafeAuraTools
    {
        public static string SafeAuraStatus(HarveySafePersonAuraService auraService)
            => auraService.BuildMcpSnapshot();

        public static string SafeAuraForceTick(HarveySafePersonAuraService auraService)
        {
            var result = auraService.DebugForceTick();

            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"applied: {result.Applied}");
            sb.AppendLine($"blockReason: {result.BlockReason ?? "(none)"}");

            if (!result.Applied)
                return sb.ToString().TrimEnd();

            sb.AppendLine("[before]");
            sb.AppendLine($"CurrentStressLoad: {result.LoadBefore}");
            sb.AppendLine($"StressRecoveryOffset: {result.OffsetBefore}");
            sb.AppendLine($"auraActive: {result.AuraActiveBefore}");
            sb.AppendLine($"forestShelterSeconds: {result.ShelterSecondsBefore}");
            sb.AppendLine("[after]");
            sb.AppendLine($"CurrentStressLoad: {result.LoadAfter}");
            sb.AppendLine($"StressRecoveryOffset: {result.OffsetAfter}");
            sb.AppendLine($"auraActive: {result.AuraActiveAfter}");
            sb.AppendLine($"appliedRecovery: {result.AppliedRecovery}");
            sb.AppendLine($"appliedShelterBonusSeconds: {result.AppliedShelterBonusSeconds}");
            sb.AppendLine("snapshot:");
            sb.Append(auraService.BuildMcpSnapshot());
            return sb.ToString().TrimEnd();
        }
    }
}
