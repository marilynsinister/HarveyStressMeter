using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Services;
using StardewModdingAPI;

namespace HarveyStressMeter.Testing
{
    /// <summary>MCP tools for forcing treatment start and episode start (EnableStressMcp only).</summary>
    internal static class McpTreatmentTools
    {
        private static readonly Dictionary<string, string> BuffDisplayNames = new()
        {
            [BuffIds.Tired] = "Усталость",
            [BuffIds.Lonely] = "Одиночество",
            [BuffIds.Thunder] = "Страх грозы",
            [BuffIds.Hunger] = "Голод",
            [BuffIds.Overwork] = "Переработка",
            [BuffIds.NoSleep] = "Недосып",
            [BuffIds.TooCold] = "Переохлаждение",
            [BuffIds.Darkness] = "Темнота",
            [BuffIds.Social] = "Социальный дискомфорт",
        };

        public static string ForceStart(
            TreatmentService treatmentService,
            StateService stateService,
            JsonElement? arguments)
        {
            if (GameStateHelper.IsEventActive())
                return $"Error: event active. stress_force_start blocked.\n\n{TestContextReporter.BuildReport()}";

            if (!TryGetString(arguments, "buff_id", out var buffId))
                return "Error: buff_id is required.";

            if (!TreatmentTopics.ImplementedBuffIds.Contains(buffId))
                return $"Error: buff_id '{buffId}' is not in TreatmentTopics.ImplementedBuffIds.";

            if (!stateService.HasBuffInGame(buffId))
                return $"Error: buff '{buffId}' is not active. Use stress_add_debuff first.";

            var treatment = stateService.GetActiveTreatment(buffId);
            if (treatment?.TreatmentStarted == true)
            {
                var sb = new StringBuilder();
                sb.AppendLine("ok: true");
                sb.AppendLine($"warning: treatment already started for {buffId}");
                sb.AppendLine(BuildTreatmentSummary(stateService, buffId));
                return sb.ToString().TrimEnd();
            }

            treatmentService.StartTreatment(buffId, BuffDisplayNames.GetValueOrDefault(buffId, buffId));

            var result = new StringBuilder();
            result.AppendLine("ok: true");
            result.AppendLine($"buff_id: {buffId}");
            result.AppendLine(BuildTreatmentSummary(stateService, buffId));
            return result.ToString().TrimEnd();
        }

        public static string EpisodeStart(
            TreatmentEpisodeService episodeService,
            StateService stateService,
            StressLoadService stressLoadService,
            JsonElement? arguments)
        {
            if (GameStateHelper.IsEventActive())
                return $"Error: event active. stress_episode_start blocked.\n\n{TestContextReporter.BuildReport()}";

            if (!TryGetString(arguments, "episode_id", out var episodeRaw))
                return "Error: episode_id is required.";

            if (!StressLoadDebugReporter.TryResolveEpisodeId(episodeRaw, out var episodeId))
            {
                return "Error: episode_id must be one of: " +
                       string.Join(", ", TreatmentEpisodeDefinitions.All.Select(d => d.EpisodeId));
            }

            if (episodeService.HasActiveTreatmentEpisode())
            {
                return "Error: active treatment episode already running. stress_reset first.";
            }

            if (!episodeService.StartTreatmentEpisode(episodeId))
            {
                return "Error: StartTreatmentEpisode returned false. " +
                       "Add matching debuff causes, wait out episode immunity, or check SMAPI log.";
            }

            stressLoadService.Recalculate();

            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"episode_id: {episodeId}");
            sb.AppendLine($"CandidateEpisode: {stressLoadService.GetCandidateEpisode() ?? "(none)"}");
            sb.AppendLine($"ActiveEpisode: {stressLoadService.GetActiveTreatmentEpisodeId() ?? "(none)"}");
            sb.AppendLine($"HasActiveTreatment: {stressLoadService.HasActiveTreatment()}");
            return sb.ToString().TrimEnd();
        }

        internal static string BuildTreatmentSummary(StateService stateService, string buffId)
        {
            var treatment = stateService.GetActiveTreatment(buffId);
            var questId = treatment?.QuestId ?? "(none)";
            var inJournal = !string.IsNullOrEmpty(treatment?.QuestId)
                            && stateService.HasQuestInGameJournal(treatment.QuestId);

            var sb = new StringBuilder();
            sb.AppendLine($"TreatmentStarted: {treatment?.TreatmentStarted ?? false}");
            sb.AppendLine($"QuestId: {questId}");
            sb.AppendLine($"QuestInJournal: {inJournal}");
            sb.AppendLine($"AwaitingHarveyReview: {treatment?.AwaitingHarveyReview ?? false}");
            sb.AppendLine($"hasBuffInGame: {stateService.HasBuffInGame(buffId)}");
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
    }
}
