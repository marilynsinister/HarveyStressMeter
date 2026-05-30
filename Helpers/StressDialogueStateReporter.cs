using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;
using StardewModdingAPI;
using StardewValley;

namespace HarveyStressMeter.Helpers
{
    /// <summary>
    /// Read-only snapshot stress dialogue pipeline для debug-команды stress_dialogue_state.
    /// </summary>
    public sealed class StressDialogueDebugSnapshot
    {
        public bool IsShowingStressDialogue { get; init; }
        public bool HasDeferredStressDialogue { get; init; }
        public string? PendingAutoStartBuffId { get; init; }
        public string? PendingAutoStartEpisodeId { get; init; }
        public string? DeferredBuffId { get; init; }
    }

    /// <summary>
    /// Компактный отчёт stress dialogue pipeline в SMAPI log (read-only).
    /// </summary>
    public sealed class StressDialogueStateReporter
    {
        private readonly SaveData _data;
        private readonly StateService _stateService;
        private readonly StressDialogueService _stressDialogueService;
        private readonly IMonitor _monitor;

        public StressDialogueStateReporter(
            SaveData data,
            StateService stateService,
            StressDialogueService stressDialogueService,
            IMonitor monitor)
        {
            _data = data;
            _stateService = stateService;
            _stressDialogueService = stressDialogueService;
            _monitor = monitor;
        }

        public void WriteReport()
        {
            foreach (var line in BuildReport().Split('\n'))
            {
                if (line.Length > 0)
                    _monitor.Log(line, LogLevel.Info);
            }
        }

        public string BuildReport()
        {
            var sb = new StringBuilder();
            void L(string message) => sb.AppendLine(message);

            var dialogue = _stressDialogueService.GetDebugSnapshot();
            var untreated = StressDebuffSelector.GetUntreatedDebuffs(_stateService, _data);
            var selected = GetStartDialogueEligibleBuff(untreated);
            var speaker = Game1.currentSpeaker?.Name ?? "null";
            var menuType = Game1.activeClickableMenu?.GetType().Name ?? "null";
            var location = Game1.currentLocation?.NameOrUniqueName ?? "null";
            var eventActive = Game1.CurrentEvent != null;
            var eventId = GameStateHelper.GetCurrentEventId();
            var dialogueBox = HarveyDevTalkHelper.GetDialogueBoxSnapshot();

            StressDialoguePipelineGuard.CanRun(
                out var guardReason,
                requireDialogueBox: false,
                requireHarveySpeaker: false);

            var stressTopics = GetStressRelatedTopics();

            var shownToday = FormatOfferShownToday(untreated);

            L("[StressDialogueState]");
            L($"Context: EventActive={eventActive}, EventUp={Game1.eventUp}, EventId={eventId ?? "null"}, Menu={menuType}, Speaker={speaker}, Location={location}, PipelineGuard={(guardReason == StressDialoguePipelineGuard.BlockReason.None ? "OK" : guardReason)}");
            L($"DialogueIsQuestion={dialogueBox.IsQuestion}, ResponseCount={dialogueBox.Responses.Count}");
            L($"Active untreated debuffs: {(untreated.Count > 0 ? string.Join(", ", untreated) : "(none)")}");
            L($"Selected by priority (start-dialogue eligible): {selected ?? "null"}");
            L($"IsShowingStressDialogue={dialogue.IsShowingStressDialogue}, HasDeferred={dialogue.HasDeferredStressDialogue}, PendingAutoStartBuffId={dialogue.PendingAutoStartBuffId ?? "null"}, DeferredBuffId={dialogue.DeferredBuffId ?? "null"}");
            L($"ShownToday: {shownToday}");

            if (stressTopics.Count > 0)
            {
                L($"Active topics: {string.Join(", ", stressTopics.Select(t => t.Item1 + (t.Item2 >= 0 ? $"({t.Item2}d)" : "")))}");
            }
            else
            {
                L("Active topics: (none)");
            }

            AppendBuffDetails(L);
            return sb.ToString().TrimEnd();
        }

        private void AppendBuffDetails(Action<string> log)
        {
            log("Buff details:");

            foreach (var buffId in TreatmentTopics.ImplementedBuffIds)
            {
                var activeInGame = _stateService.HasBuffInGame(buffId);
                var treatment = _stateService.GetActiveTreatment(buffId);
                var started = treatment?.TreatmentStarted ?? false;
                var completed = treatment != null && (treatment.IsCured || treatment.IsCompleted);
                var awaitingReview = treatment?.AwaitingHarveyReview ?? false;
                var severity = GetSeverityLabel(buffId);

                if (!activeInGame && treatment == null)
                    continue;

                var objectivesCompleted = treatment?.ObjectivesCompleted ?? false;
                var nextStep = TreatmentNextStep.Resolve(treatment, activeInGame);
                var detailPart = treatment != null
                    ? $", questId={treatment.QuestId ?? "(empty)"}, objectivesCompleted={objectivesCompleted}, awaitingHarveyReview={awaitingReview}"
                      + (treatment.ReadyForReviewDate != null
                          ? $", readyForReviewDate={treatment.ReadyForReviewDate}"
                          : "")
                      + $", nextStep={nextStep}"
                    : activeInGame
                        ? $", nextStep={nextStep}"
                        : "";

                log($"  {buffId}: active={activeInGame}, treatmentStarted={started}, treatmentCompleted={completed}{detailPart}{severity}");
            }

            foreach (var levelBuff in new[] { BuffIds.DarknessLevel1, BuffIds.DarknessLevel2, BuffIds.DarknessLevel3 })
            {
                if (!_stateService.HasBuffInGame(levelBuff))
                    continue;

                log($"  {levelBuff}: active=true, treatmentStarted=n/a, treatmentCompleted=n/a, level={levelBuff[^1]}");
            }
        }

        private string? GetStartDialogueEligibleBuff(IReadOnlyList<string> untreated)
        {
            foreach (var buffId in StressDebuffSelector.PriorityOrder)
            {
                if (untreated.Contains(buffId))
                    return buffId;
            }

            foreach (var buffId in untreated)
            {
                if (DarknessLegacyHelper.IsDarknessLevelBuff(buffId))
                    return buffId;
            }

            return null;
        }

        private string GetSeverityLabel(string buffId)
        {
            if (buffId != BuffIds.Darkness)
                return "";

            var fear = _data.Darkness.FearLevel;
            return $", fearLevel={fear}";
        }

        private string FormatOfferShownToday(IReadOnlyList<string> buffIds)
        {
            if (buffIds.Count == 0)
                return "(no untreated debuffs)";

            var parts = new List<string>();
            foreach (var buffId in buffIds)
            {
                parts.Add($"{ShortBuff(buffId)}={_stateService.WasTreatmentOfferShownToday(buffId)}");
            }

            return string.Join(", ", parts);
        }

        private static string ShortBuff(string buffId)
        {
            return buffId.StartsWith("buffStress", System.StringComparison.Ordinal)
                ? buffId["buffStress".Length..]
                : buffId;
        }

        private static List<(string Key, int Days)> GetStressRelatedTopics()
        {
            var result = new List<(string, int)>();

            foreach (var key in Game1.player.activeDialogueEvents.Keys)
            {
                if (!IsStressRelatedTopic(key))
                    continue;

                var days = Game1.player.activeDialogueEvents[key];
                result.Add((key, days));
            }

            result.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
            return result;
        }

        private static bool IsStressRelatedTopic(string key)
        {
            if (!key.Contains("Stress", System.StringComparison.OrdinalIgnoreCase)
                && !key.StartsWith("topicDarkness", System.StringComparison.Ordinal))
            {
                return false;
            }

            return key.StartsWith("topicStress", System.StringComparison.Ordinal)
                || key.StartsWith("topicDarkness", System.StringComparison.Ordinal)
                || key == TopicIds.TreatmentStarted;
        }
    }
}
