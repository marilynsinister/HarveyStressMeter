using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;

namespace HarveyStressMeter.Testing
{
    /// <summary>MCP debug tools for stress HUD and treatment snapshots (EnableStressMcp only).</summary>
    internal static class McpHudTools
    {
        public static string HudSnapshot(StressMeterHudService hudService)
            => hudService.BuildMcpSnapshot();

        public static string TreatmentSnapshot(
            SaveData data,
            StateService stateService,
            StressLoadService stressLoadService,
            TreatmentEpisodeService episodeService,
            StressDialogueService stressDialogueService)
            => TreatmentDebugReporter.BuildMcpSnapshot(
                data,
                stateService,
                stressLoadService,
                episodeService,
                stressDialogueService);
    }
}
