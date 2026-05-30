using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;

namespace HarveyStressMeter.Helpers
{
    /// <summary>
    /// Подробный debug-вывод состояния stress treatment (SMAPI log + HUD).
    /// </summary>
    public static class TreatmentDebugReporter
    {
        public const string DevPrefix = "[DEV/TEST]";

        public static void LogActiveTreatments(IMonitor monitor, SaveData data, StateService stateService)
        {
            monitor.Log($"{DevPrefix} === stress_debug_state: active treatments ===", LogLevel.Info);

            if (data.StressState.ActiveTreatments.Count == 0)
            {
                monitor.Log($"{DevPrefix} (no active treatments in save)", LogLevel.Info);
                LogUntreatedDebuffs(monitor, stateService);
                return;
            }

            foreach (var treatment in data.StressState.ActiveTreatments.Values.OrderBy(t => t.TreatmentKey))
                LogTreatmentLine(monitor, treatment, stateService);

            LogUntreatedDebuffs(monitor, stateService);
        }

        public static void LogTreatmentDetail(IMonitor monitor, StateService stateService, string buffId)
        {
            monitor.Log($"{DevPrefix} --- treatment detail: {buffId} ---", LogLevel.Info);

            var treatment = stateService.GetActiveTreatment(buffId);
            if (treatment == null)
            {
                monitor.Log($"{DevPrefix} no active treatment record for buffId={buffId}", LogLevel.Info);
                monitor.Log($"{DevPrefix} hasBuffInGame={stateService.HasBuffInGame(buffId)}", LogLevel.Info);
                monitor.Log(
                    $"{DevPrefix} next step: {TreatmentNextStep.Resolve(null, stateService.HasBuffInGame(buffId))}",
                    LogLevel.Info);
                return;
            }

            LogTreatmentLine(monitor, treatment, stateService);
        }

        public static string BuildActiveTreatmentsSummary(SaveData data, StateService stateService)
        {
            var sb = new StringBuilder();

            if (data.StressState.ActiveTreatments.Count == 0)
            {
                sb.AppendLine("(no active treatments)");
                return sb.ToString().TrimEnd();
            }

            foreach (var treatment in data.StressState.ActiveTreatments.Values.OrderBy(t => t.TreatmentKey))
                sb.AppendLine(FormatTreatmentLine(treatment, stateService));

            return sb.ToString().TrimEnd();
        }

        /// <summary>DEV/MCP: machine-readable treatment snapshot for AI tests.</summary>
        public static string BuildMcpSnapshot(
            SaveData data,
            StateService stateService,
            StressLoadService stressLoadService,
            TreatmentEpisodeService episodeService,
            StressDialogueService stressDialogueService)
        {
            var episode = data.ActiveTreatmentEpisode;
            var selection = episodeService.EvaluateSelection();
            var dialogue = stressDialogueService.GetDebugSnapshot();
            var untreated = StressDebuffSelector.GetUntreatedDebuffs(stateService);
            var activeDebuffs = GetActiveDebuffs(stateService);
            var stressTopics = ListStressTopics();
            var stressQuests = ListStressQuests(data, stateService, episode);

            var sb = new StringBuilder();
            sb.AppendLine("ok: true");
            sb.AppendLine($"ActiveTreatments count: {data.StressState.ActiveTreatments.Count}");
            sb.AppendLine($"UntreatedDebuffs: {(untreated.Count > 0 ? string.Join(", ", untreated) : "(none)")}");
            sb.AppendLine($"ActiveDebuffs: {(activeDebuffs.Count > 0 ? string.Join(", ", activeDebuffs) : "(none)")}");
            sb.AppendLine($"CurrentEpisode: {episode?.EpisodeId ?? "(none)"}");
            sb.AppendLine($"CandidateEpisode: {stressLoadService.GetCandidateEpisode() ?? selection.EpisodeId ?? "(none)"}");
            sb.AppendLine($"PendingTreatment buffId: {dialogue.PendingAutoStartBuffId ?? "(none)"}");
            sb.AppendLine($"PendingTreatment episodeId: {dialogue.PendingAutoStartEpisodeId ?? "(none)"}");
            sb.AppendLine($"SelectionAction: {selection.Action}");
            sb.AppendLine($"SelectionReason: {selection.Reason ?? "(none)"}");
            sb.AppendLine($"AwaitingHarveyReview: {stressLoadService.IsAwaitingHarveyReview()}");
            sb.AppendLine($"StressQuests: {(stressQuests.Count > 0 ? string.Join(", ", stressQuests) : "(none)")}");
            sb.AppendLine($"StressTopics: {(stressTopics.Count > 0 ? string.Join(", ", stressTopics) : "(none)")}");

            if (data.StressState.ActiveTreatments.Count == 0)
            {
                sb.AppendLine("treatments: (none)");
                return sb.ToString().TrimEnd();
            }

            sb.AppendLine("treatments:");
            foreach (var treatment in data.StressState.ActiveTreatments.Values.OrderBy(t => t.TreatmentKey))
                sb.AppendLine(FormatTreatmentMcpBlock(treatment, stateService, data));

            return sb.ToString().TrimEnd();
        }

        /// <summary>DEV/MCP: treatment snapshot + StressLoad ActiveCauses and episode fields.</summary>
        public static string BuildTreatmentDebugSnapshot(
            SaveData data,
            StateService stateService,
            StressLoadService stressLoadService,
            TreatmentEpisodeService episodeService,
            StressDialogueService stressDialogueService)
        {
            var sb = new StringBuilder();
            sb.AppendLine(BuildMcpSnapshot(data, stateService, stressLoadService, episodeService, stressDialogueService));
            sb.AppendLine("--- ActiveCauses ---");

            if (stressLoadService == null)
            {
                sb.AppendLine("(stressLoadService null)");
                return sb.ToString().TrimEnd();
            }

            var causes = stressLoadService.GetActiveCauses()
                .Where(kvp => kvp.Value.IsActive)
                .OrderByDescending(kvp => kvp.Value.Weight)
                .ToList();

            if (causes.Count == 0)
                sb.AppendLine("(none)");
            else
            {
                foreach (var (causeId, cause) in causes)
                {
                    sb.AppendLine(
                        $"  {causeId}: weight={cause.Weight}, buff={cause.SourceBuffId ?? "(none)"}, " +
                        $"severe={cause.IsSevere}");
                }
            }

            var episode = data.ActiveTreatmentEpisode;
            sb.AppendLine($"ActiveTreatmentEpisode.AwaitingHarveyReview: {episode?.AwaitingHarveyReview ?? false}");
            sb.AppendLine($"ActiveTreatmentEpisode.EpisodeId: {episode?.EpisodeId ?? "(none)"}");
            return sb.ToString().TrimEnd();
        }

        private static List<string> GetActiveDebuffs(StateService stateService)
        {
            var result = new List<string>();
            foreach (var buffId in TreatmentTopics.ImplementedBuffIds)
            {
                if (stateService.HasBuffInGame(buffId))
                    result.Add(buffId);
            }

            return result;
        }

        private static List<string> ListStressTopics()
        {
            var result = new List<string>();
            foreach (var topicId in Game1.player.activeDialogueEvents.Keys)
            {
                if (topicId.Contains("topicStress", StringComparison.OrdinalIgnoreCase))
                    result.Add($"{topicId}({Game1.player.activeDialogueEvents[topicId]})");
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        private static List<string> ListStressQuests(
            SaveData data,
            StateService stateService,
            TreatmentEpisodeState? episode)
        {
            var quests = new HashSet<string>(StringComparer.Ordinal);

            if (!string.IsNullOrEmpty(episode?.QuestId))
                quests.Add(episode.QuestId);

            foreach (var treatment in data.StressState.ActiveTreatments.Values)
            {
                if (!string.IsNullOrEmpty(treatment.QuestId))
                    quests.Add(treatment.QuestId);
            }

            return quests
                .Where(q => Game1.player.hasQuest(q))
                .OrderBy(q => q, StringComparer.Ordinal)
                .ToList();
        }

        private static string FormatTreatmentMcpBlock(TreatmentState treatment, StateService stateService, SaveData data)
        {
            var hasBuff = stateService.HasBuffInGame(treatment.BuffId);
            var reviewTopic = TreatmentTopics.GetReadyForReviewTopic(treatment.BuffId);
            var reviewTopicActive = reviewTopic != null && ConversationHelper.HasTopic(reviewTopic);
            StressCauses.TryGetCauseForBuff(treatment.BuffId, out var causeId);
            var startDate = treatment.TreatmentStartedDate ?? treatment.IssuedDate;
            var daysActive = Math.Max(0, SDate.Now().DaysSinceStart - startDate.DaysSinceStart);
            var missedPrescription = hasBuff && !treatment.TreatmentStarted && !treatment.IsCured;
            var questId = treatment.QuestId ?? "(empty)";
            var questInJournal = !string.IsNullOrEmpty(treatment.QuestId) && Game1.player.hasQuest(treatment.QuestId);

            return string.Join('\n',
            [
                $"  - treatmentKey={treatment.TreatmentKey}",
                $"    buffId={treatment.BuffId}",
                $"    cause={causeId ?? "(none)"}",
                $"    started={treatment.TreatmentStarted}",
                $"    completed={treatment.IsCompleted}",
                $"    cured={treatment.IsCured}",
                $"    AwaitingHarveyReview={treatment.AwaitingHarveyReview}",
                $"    objectivesCompleted={treatment.ObjectivesCompleted}",
                $"    questId={questId}",
                $"    questInJournal={questInJournal}",
                FormatLegacyProgressLine(treatment, data),
                $"    currentObjective={FormatQuestCurrentObjective(treatment.QuestId)}",
                $"    daysActive={daysActive}",
                $"    hasBuffInGame={hasBuff}",
                $"    readyForReviewTopic={reviewTopic ?? "(n/a)"} active={reviewTopicActive}",
                $"    missedPrescription={missedPrescription}",
                $"    nextStep={TreatmentNextStep.Resolve(treatment, hasBuff)}",
            ]);
        }

        private static string FormatQuestCurrentObjective(string? questId)
        {
            if (string.IsNullOrEmpty(questId))
                return "(no questId)";

            if (!Game1.player.hasQuest(questId))
                return "(quest not in journal)";

            var quest = Game1.player.questLog.FirstOrDefault(q => q.id.Value == questId);
            if (quest == null)
                return "(quest missing from questLog)";

            var objective = quest.currentObjective?.Trim();
            return string.IsNullOrEmpty(objective) ? "(empty)" : objective;
        }

        private static string FormatLegacyProgressLine(TreatmentState treatment, SaveData data)
        {
            var p = treatment.Progress;
            if (p == null)
                return "    progress=(null)";

            return treatment.BuffId switch
            {
                BuffIds.Lonely => $"    progress: TalkedUniqueToday={p.TalkedUniqueToday}",
                BuffIds.Hunger => $"    progress: AteAnyFood={p.AteAnyFood}",
                BuffIds.NoSleep =>
                    $"    progress: EarlySleepStreak={p.EarlySleepStreak}, NoSleepLateObjectivePending={data.NoSleepLateObjectivePending}",
                BuffIds.Overwork =>
                    $"    progress: OverworkBreaksToday={data.OverworkBreaksToday}, OverworkBreakSeconds={data.OverworkBreakSeconds}, OverworkBreakActive={data.OverworkBreakActive}",
                BuffIds.TooCold => $"    progress: WarmSeconds={p.WarmSeconds}",
                _ => $"    progress: WarmSeconds={p.WarmSeconds}, AteAnyFood={p.AteAnyFood}, TalkedUniqueToday={p.TalkedUniqueToday}",
            };
        }

        public static void ShowHud(string message)
        {
            if (!Context.IsWorldReady)
                return;

            Game1.addHUDMessage(new HUDMessage($"{DevPrefix} {message}", HUDMessage.newQuest_type));
        }

        private static void LogUntreatedDebuffs(IMonitor monitor, StateService stateService)
        {
            var untreated = StressDebuffSelector.GetUntreatedDebuffs(stateService);
            monitor.Log(
                $"{DevPrefix} untreated debuffs (no quest yet): {(untreated.Count > 0 ? string.Join(", ", untreated) : "(none)")}",
                LogLevel.Info);
        }

        private static void LogTreatmentLine(IMonitor monitor, TreatmentState treatment, StateService stateService)
        {
            foreach (var line in FormatTreatmentLine(treatment, stateService).Split('\n'))
                monitor.Log($"{DevPrefix} {line}", LogLevel.Info);
        }

        private static string FormatTreatmentLine(TreatmentState treatment, StateService stateService)
        {
            var hasBuff = stateService.HasBuffInGame(treatment.BuffId);
            var nextStep = TreatmentNextStep.Resolve(treatment, hasBuff);
            var reviewTopic = TreatmentTopics.GetReadyForReviewTopic(treatment.BuffId);
            var reviewTopicActive = reviewTopic != null && ConversationHelper.HasTopic(reviewTopic);

            return string.Join('\n',
            [
                $"[{treatment.TreatmentKey}]",
                $"  buffId={treatment.BuffId}",
                $"  questId={treatment.QuestId ?? "(empty)"}",
                $"  TreatmentStarted={treatment.TreatmentStarted}",
                $"  ObjectivesCompleted={treatment.ObjectivesCompleted}",
                $"  AwaitingHarveyReview={treatment.AwaitingHarveyReview}",
                $"  IsCured={treatment.IsCured}",
                $"  IsCompleted={treatment.IsCompleted}",
                $"  ReadyForReviewDate={treatment.ReadyForReviewDate?.ToString() ?? "(null)"}",
                $"  hasBuffInGame={hasBuff}",
                $"  readyForReviewTopic={reviewTopic ?? "(n/a)"} active={reviewTopicActive}",
                $"  next step: {nextStep}",
            ]);
        }
    }
}
