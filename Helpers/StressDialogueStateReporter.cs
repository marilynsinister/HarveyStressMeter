using System.Collections.Generic;
using System.Linq;
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
        public string? PendingBuffIdForTreatment { get; init; }
        public string? DeferredBuffId { get; init; }
        public string PendingConsentResult { get; init; } = "None";
        public bool ResponseAlreadyRecorded { get; init; }
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
            var dialogue = _stressDialogueService.GetDebugSnapshot();
            var untreated = StressDebuffSelector.GetUntreatedDebuffs(_stateService);
            var selected = GetConsentEligibleBuff(untreated);
            var speaker = Game1.currentSpeaker?.Name ?? "null";
            var menuType = Game1.activeClickableMenu?.GetType().Name ?? "null";
            var location = Game1.currentLocation?.NameOrUniqueName ?? "null";
            var eventActive = Game1.CurrentEvent != null;

            StressDialoguePipelineGuard.CanRun(
                out var guardReason,
                requireDialogueBox: false,
                requireHarveySpeaker: false);

            var stressTopics = GetStressRelatedTopics();

            var shownToday = FormatOfferDeclineToday(isOffer: true, untreated);
            var declinedToday = FormatOfferDeclineToday(isOffer: false, untreated);

            _monitor.Log("[StressDialogueState]", LogLevel.Info);
            _monitor.Log(
                $"Context: EventActive={eventActive}, EventUp={Game1.eventUp}, Menu={menuType}, Speaker={speaker}, Location={location}, PipelineGuard={(guardReason == StressDialoguePipelineGuard.BlockReason.None ? "OK" : guardReason)}",
                LogLevel.Info);
            _monitor.Log(
                $"Active untreated debuffs: {(untreated.Count > 0 ? string.Join(", ", untreated) : "(none)")}",
                LogLevel.Info);
            _monitor.Log(
                $"Selected by priority (consent-eligible): {selected ?? "null"}",
                LogLevel.Info);
            _monitor.Log(
                $"IsShowingStressDialogue={dialogue.IsShowingStressDialogue}, HasDeferred={dialogue.HasDeferredStressDialogue}, PendingBuffId={dialogue.PendingBuffIdForTreatment ?? "null"}, DeferredBuffId={dialogue.DeferredBuffId ?? "null"}, Consent={dialogue.PendingConsentResult}, ResponseRecorded={dialogue.ResponseAlreadyRecorded}",
                LogLevel.Info);
            _monitor.Log($"ShownToday: {shownToday}", LogLevel.Info);
            _monitor.Log($"DeclinedToday: {declinedToday}", LogLevel.Info);

            if (stressTopics.Count > 0)
            {
                _monitor.Log(
                    $"Active topics: {string.Join(", ", stressTopics.Select(t => t.Item1 + (t.Item2 >= 0 ? $"({t.Item2}d)" : "")))}",
                    LogLevel.Info);
            }
            else
            {
                _monitor.Log("Active topics: (none)", LogLevel.Info);
            }

            WriteBuffDetails();
        }

        private void WriteBuffDetails()
        {
            _monitor.Log("Buff details:", LogLevel.Info);

            foreach (var buffId in TreatmentTopics.ImplementedBuffIds)
            {
                var activeInGame = _stateService.HasBuffInGame(buffId);
                var treatment = _stateService.GetActiveTreatment(buffId);
                var started = treatment?.TreatmentStarted ?? false;
                var completed = treatment != null && (treatment.IsCured || treatment.IsCompleted);
                var severity = GetSeverityLabel(buffId);

                if (!activeInGame && treatment == null)
                    continue;

                _monitor.Log(
                    $"  {buffId}: active={activeInGame}, treatmentStarted={started}, treatmentCompleted={completed}{severity}",
                    LogLevel.Info);
            }

            foreach (var levelBuff in new[] { BuffIds.DarknessLevel1, BuffIds.DarknessLevel2, BuffIds.DarknessLevel3 })
            {
                if (!_stateService.HasBuffInGame(levelBuff))
                    continue;

                _monitor.Log(
                    $"  {levelBuff}: active=true, treatmentStarted=n/a, treatmentCompleted=n/a, level={levelBuff[^1]}",
                    LogLevel.Info);
            }
        }

        private string? GetConsentEligibleBuff(IReadOnlyList<string> untreated)
        {
            foreach (var buffId in StressDebuffSelector.PriorityOrder)
            {
                if (!untreated.Contains(buffId))
                    continue;

                if (_stateService.WasTreatmentDeclinedToday(buffId))
                    continue;

                if (_stateService.WasTreatmentOfferShownToday(buffId))
                    continue;

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

        private string FormatOfferDeclineToday(bool isOffer, IReadOnlyList<string> buffIds)
        {
            if (buffIds.Count == 0)
                return "(no untreated debuffs)";

            var parts = new List<string>();
            foreach (var buffId in buffIds)
            {
                var value = isOffer
                    ? _stateService.WasTreatmentOfferShownToday(buffId)
                    : _stateService.WasTreatmentDeclinedToday(buffId);
                parts.Add($"{ShortBuff(buffId)}={value}");
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
