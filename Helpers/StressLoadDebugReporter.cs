using System.Linq;
using System.Text;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;
using StardewModdingAPI;

namespace HarveyStressMeter.Helpers
{
    /// <summary>Debug snapshot StressLoad / TreatmentEpisode / LightningFright.</summary>
    public static class StressLoadDebugReporter
    {
        public static string BuildFullReport(
            SaveData data,
            StressLoadService stressLoadService,
            TreatmentEpisodeService episodeService,
            ThunderFlashbackService flashbackService,
            HarveyFlashbackRescueService? rescueService,
            HarveyCareTrustService? trustService,
            HarveySafePersonAuraService? safeAuraService,
            StressSystemsCoordinator? systemsCoordinator,
            StressGameplayEffectService? gameplayEffectService,
            StateService stateService)
        {
            var sb = new StringBuilder();
            var load = stressLoadService.GetCurrentStressLoad();
            var severity = stressLoadService.GetSeverity();
            var selection = episodeService.EvaluateSelection();
            var episode = data.ActiveTreatmentEpisode;
            var flashback = flashbackService.State;

            sb.AppendLine("=== StressLoad ===");
            sb.AppendLine($"CurrentStressLoad: {load} (raw {stressLoadService.GetRawStressLoad()})");
            sb.AppendLine($"Severity: {severity}");
            sb.AppendLine($"Primary cause: {stressLoadService.GetPrimaryCause() ?? "(none)"}");
            sb.AppendLine($"HasActiveTreatment: {stressLoadService.HasActiveTreatment()}");
            sb.AppendLine($"AwaitingHarveyReview (load flags): {stressLoadService.IsAwaitingHarveyReview()}");

            sb.AppendLine("Active causes:");
            var causes = stressLoadService.GetActiveCauses()
                .Where(kvp => kvp.Value.IsActive)
                .OrderByDescending(kvp => kvp.Value.Weight)
                .ToList();

            if (causes.Count == 0)
                sb.AppendLine("  (none)");
            else
            {
                foreach (var (causeId, cause) in causes)
                {
                    sb.AppendLine(
                        $"  {causeId}: weight={cause.Weight}, buff={cause.SourceBuffId}, " +
                        $"severe={cause.IsSevere}, selfResolve={cause.CanSelfResolve}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("=== Episodes ===");
            sb.AppendLine($"Candidate episode: {stressLoadService.GetCandidateEpisode() ?? "(none)"}");
            sb.AppendLine($"Selection action: {selection.Action}");
            sb.AppendLine($"Selection episode: {selection.EpisodeId ?? "(none)"}");
            sb.AppendLine($"Selection reason: {selection.Reason ?? "(none)"}");
            sb.AppendLine($"Active treatment episode (load): {stressLoadService.GetActiveTreatmentEpisodeId() ?? "(none)"}");

            if (episode != null)
            {
                sb.AppendLine("ActiveTreatmentEpisode state:");
                sb.AppendLine($"  EpisodeId={episode.EpisodeId}");
                sb.AppendLine($"  QuestId={episode.QuestId}");
                sb.AppendLine($"  TreatmentStarted={episode.TreatmentStarted}");
                sb.AppendLine($"  ObjectivesCompleted={episode.ObjectivesCompleted}");
                sb.AppendLine($"  AwaitingHarveyReview={episode.AwaitingHarveyReview}");
                sb.AppendLine($"  IsCompleted={episode.IsCompleted}");
                sb.AppendLine($"  PrimaryCause={episode.PrimaryCauseId ?? "(none)"}");
                sb.AppendLine($"  Related=[{string.Join(", ", episode.RelatedCauseIds)}]");
            }
            else
            {
                sb.AppendLine("ActiveTreatmentEpisode state: (none)");
            }

            sb.AppendLine();
            sb.AppendLine("=== Next step ===");
            sb.AppendLine(ResolveNextStep(data, episode, selection, stateService));

            sb.AppendLine();
            sb.AppendLine(flashbackService.BuildDebugSnapshot());

            if (rescueService != null)
            {
                sb.AppendLine();
                sb.AppendLine(rescueService.BuildDebugSnapshot());
            }

            if (trustService != null)
            {
                sb.AppendLine();
                sb.AppendLine(trustService.BuildDebugSnapshot());
            }

            if (safeAuraService != null)
            {
                sb.AppendLine();
                sb.AppendLine(safeAuraService.BuildDebugSnapshot());
            }

            if (systemsCoordinator != null)
            {
                sb.AppendLine();
                sb.AppendLine(systemsCoordinator.BuildIntegrationSnapshot());
            }

            if (gameplayEffectService != null)
            {
                var (stam, speed) = gameplayEffectService.LastAppliedPenalties;
                sb.AppendLine("=== Gameplay penalties ===");
                sb.AppendLine($"Applied stamina penalty: {stam}");
                sb.AppendLine($"Applied speed penalty: {speed}");
            }

            sb.AppendLine();
            sb.AppendLine("=== Legacy treatments ===");
            sb.AppendLine(TreatmentDebugReporter.BuildActiveTreatmentsSummary(data, stateService));

            return sb.ToString().TrimEnd();
        }

        public static void LogFullState(
            IMonitor monitor,
            SaveData data,
            StressLoadService stressLoadService,
            TreatmentEpisodeService episodeService,
            ThunderFlashbackService flashbackService,
            HarveyFlashbackRescueService? rescueService,
            HarveyCareTrustService? trustService,
            HarveySafePersonAuraService? safeAuraService,
            StressSystemsCoordinator? systemsCoordinator,
            StressGameplayEffectService? gameplayEffectService,
            StateService stateService,
            string? header = null)
        {
            if (!string.IsNullOrEmpty(header))
                monitor.Log($"{TreatmentDebugReporter.DevPrefix} {header}", LogLevel.Info);

            foreach (var line in BuildFullReport(
                         data,
                         stressLoadService,
                         episodeService,
                         flashbackService,
                         rescueService,
                         trustService,
                         safeAuraService,
                         systemsCoordinator,
                         gameplayEffectService,
                         stateService)
                     .Split('\n'))
            {
                monitor.Log($"{TreatmentDebugReporter.DevPrefix} {line}", LogLevel.Info);
            }
        }

        public static string BuildHudSummary(
            StressLoadService stressLoadService,
            TreatmentEpisodeService episodeService,
            SaveData data)
        {
            var load = stressLoadService.GetCurrentStressLoad();
            var severity = stressLoadService.GetSeverity();
            var candidate = stressLoadService.GetCandidateEpisode() ?? "-";
            var active = data.ActiveTreatmentEpisode?.EpisodeId
                ?? stressLoadService.GetActiveTreatmentEpisodeId()
                ?? "-";
            var review = data.ActiveTreatmentEpisode?.AwaitingHarveyReview == true
                || stressLoadService.IsAwaitingHarveyReview();

            return $"load={load} {severity}, episode={active}, candidate={candidate}, review={review}";
        }

        private static string ResolveNextStep(
            SaveData data,
            TreatmentEpisodeState? episode,
            EpisodeSelectionResult selection,
            StateService stateService)
        {
            if (episode?.AwaitingHarveyReview == true)
                return TreatmentNextStep.Review;

            if (episode?.TreatmentStarted == true && episode.IsActiveEpisode())
                return TreatmentNextStep.Objective;

            return selection.Action switch
            {
                EpisodeSelectionAction.AwaitingReview => TreatmentNextStep.Review,
                EpisodeSelectionAction.ReminderOnly => TreatmentNextStep.Objective,
                EpisodeSelectionAction.StartEpisode => TreatmentNextStep.Prescription,
                EpisodeSelectionAction.AmbientOnly => "Ambient stress notice (no quest)",
                _ when StressDebuffSelector.GetUntreatedDebuffs(stateService).Count > 0
                    => TreatmentNextStep.Prescription,
                _ => "(no active stress flow)",
            };
        }

        public static bool TryResolveCauseId(string input, out string causeId)
        {
            causeId = input.Trim();
            if (string.IsNullOrEmpty(causeId))
                return false;

            foreach (var known in StressCauses.BaseWeights.Keys)
            {
                if (string.Equals(known, causeId, System.StringComparison.OrdinalIgnoreCase))
                {
                    causeId = known;
                    return true;
                }
            }

            return false;
        }

        public static bool TryResolveEpisodeId(string input, out string episodeId)
        {
            episodeId = input.Trim();
            if (string.IsNullOrEmpty(episodeId))
                return false;

            foreach (var def in TreatmentEpisodeDefinitions.All)
            {
                if (string.Equals(def.EpisodeId, episodeId, System.StringComparison.OrdinalIgnoreCase))
                {
                    episodeId = def.EpisodeId;
                    return true;
                }
            }

            return false;
        }
    }
}
