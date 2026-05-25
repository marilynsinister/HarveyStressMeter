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
    /// hs.debug v2 — сравнение mod state vs реальное состояние игры (H-02–H-10).
    /// </summary>
    public sealed class HsDebugReporter
    {
        private readonly SaveData _data;
        private readonly StateService _stateService;
        private readonly IMonitor _monitor;

        private static readonly (string BuffId, string QuestId)[] CoreStressPairs =
        {
            (BuffIds.Tired, QuestIds.Tired),
            (BuffIds.Lonely, QuestIds.Lonely),
            (BuffIds.Thunder, QuestIds.Thunder),
            (BuffIds.Hunger, QuestIds.Hunger),
            (BuffIds.Overwork, QuestIds.Overwork),
            (BuffIds.NoSleep, QuestIds.NoSleep),
            (BuffIds.TooCold, QuestIds.TooCold),
            (BuffIds.Social, QuestIds.Social),
            (BuffIds.Darkness, QuestIds.Darkness),
        };

        private static readonly string[] DarknessLevelBuffIds =
        {
            BuffIds.DarknessLevel1,
            BuffIds.DarknessLevel2,
            BuffIds.DarknessLevel3,
        };

        private static readonly string[] ServiceBuffIds =
        {
            BuffIds.CareAura,
            BuffIds.LightAndSafe,
            BuffIds.OverworkBreak,
            BuffIds.RestingAtHome,
            BuffIds.CalmingAtHospital,
        };

        private static readonly string[] StressTopicIds =
        {
            TopicIds.StressTired,
            TopicIds.StressLonely,
            TopicIds.StressThunder,
            TopicIds.StressHunger,
            TopicIds.StressOverwork,
            TopicIds.StressNoSleep,
            TopicIds.StressTooCold,
            TopicIds.StressDarkness,
            TopicIds.StressSocial,
            TopicIds.OverworkBreakActive,
            TopicIds.OverworkBreakInterrupted,
        };

        private static readonly string[] TreatmentStartedTopicIds =
        {
            TopicIds.TreatmentStarted,
            TopicIds.TreatmentStartTired,
            TopicIds.TreatmentStartLonely,
            TopicIds.TreatmentStartThunder,
            TopicIds.TreatmentStartHunger,
            TopicIds.TreatmentStartOverwork,
            TopicIds.TreatmentStartNoSleep,
            TopicIds.TreatmentStartTooCold,
            TopicIds.TreatmentStartSocial,
            TopicIds.TreatmentStartDarkness,
            "topicDarknessTherapyStart",
        };

        private static readonly string[] CuredTopicIds =
        {
            "topicStressTreatmentTiredCured",
            "topicStressTreatmentLonelyCured",
            "topicStressTreatmentThunderCured",
            "topicStressTreatmentHungerCured",
            "topicStressTreatmentOverworkCured",
            "topicStressTreatmentNoSleepCured",
            "topicStressTreatmentTooColdCured",
            "topicStressTreatmentSocialCured",
            "topicStressTreatmentDarknessCured",
            "topicDarknessStep1Complete",
            "topicDarknessStep2Complete",
            "topicDarknessFullyCured",
        };

        public HsDebugReporter(SaveData data, StateService stateService, IMonitor monitor)
        {
            _data = data;
            _stateService = stateService;
            _monitor = monitor;
        }

        public void WriteFullReport()
        {
            _monitor.Log("=== hs.debug v2 — HarveyStressMeter ===", LogLevel.Info);
            WriteModState();
            WriteRealGameBuffs();
            WriteRealQuestJournal();
            WriteTopics();
            WriteDarkness();
            WriteProblems();
            _monitor.Log("=== END hs.debug v2 ===", LogLevel.Info);
        }

        private void WriteModState()
        {
            _monitor.Log("\n--- MOD STATE ---", LogLevel.Info);
            _monitor.Log($"ActiveTreatments count: {_data.StressState.ActiveTreatments.Count}", LogLevel.Info);

            if (_data.StressState.ActiveTreatments.Count == 0)
            {
                _monitor.Log("  (no active treatments in save)", LogLevel.Info);
                return;
            }

            foreach (var treatment in _data.StressState.ActiveTreatments.Values.OrderBy(t => t.TreatmentKey))
            {
                _monitor.Log($"  TreatmentKey: {treatment.TreatmentKey}", LogLevel.Info);
                _monitor.Log($"    BuffId: {treatment.BuffId}", LogLevel.Info);
                _monitor.Log($"    QuestId: {NullOrValue(treatment.QuestId)}", LogLevel.Info);
                _monitor.Log($"    TreatmentStarted: {treatment.TreatmentStarted}", LogLevel.Info);
                _monitor.Log($"    IsCured: {treatment.IsCured}", LogLevel.Info);
                _monitor.Log($"    IsCompleted: {treatment.IsCompleted}", LogLevel.Info);
                _monitor.Log($"    AddedToGameLog: {treatment.AddedToGameLog}", LogLevel.Info);
                _monitor.Log($"    IssuedDate: {treatment.IssuedDate}", LogLevel.Info);
                if (treatment.TreatmentStartedDate != null)
                    _monitor.Log($"    TreatmentStartedDate: {treatment.TreatmentStartedDate}", LogLevel.Info);

                if (treatment.Progress != null)
                    _monitor.Log($"    Progress: {FormatProgress(treatment.Progress)}", LogLevel.Info);
                else
                    _monitor.Log("    Progress: (null)", LogLevel.Info);
            }

            _monitor.Log($"ActiveTreatmentsByBuff index entries: {_data.StressState.ActiveTreatmentsByBuff.Count}", LogLevel.Info);
            foreach (var (buffId, keys) in _data.StressState.ActiveTreatmentsByBuff.OrderBy(k => k.Key))
                _monitor.Log($"  {buffId} → [{string.Join(", ", keys)}]", LogLevel.Info);
        }

        private void WriteRealGameBuffs()
        {
            _monitor.Log("\n--- REAL GAME BUFFS (Game1.player.hasBuff) ---", LogLevel.Info);

            _monitor.Log("Stress debuffs:", LogLevel.Info);
            foreach (var (buffId, questId) in CoreStressPairs)
            {
                bool inGame = _stateService.HasBuffInGame(buffId);
                bool inState = _stateService.HasActiveTreatmentState(buffId);
                _monitor.Log($"  {buffId} ({questId}): hasBuff={inGame}, modState={inState}", LogLevel.Info);
            }

            foreach (var buffId in DarknessLevelBuffIds)
            {
                bool inGame = _stateService.HasBuffInGame(buffId);
                _monitor.Log($"  {buffId}: hasBuff={inGame}", LogLevel.Info);
            }

            _monitor.Log("Service / quest buffs:", LogLevel.Info);
            foreach (var buffId in ServiceBuffIds)
                _monitor.Log($"  {buffId}: hasBuff={_stateService.HasBuffInGame(buffId)}", LogLevel.Info);
        }

        private void WriteRealQuestJournal()
        {
            _monitor.Log("\n--- REAL QUEST JOURNAL (Game1.player.questLog) ---", LogLevel.Info);

            foreach (var (buffId, questId) in CoreStressPairs)
            {
                var quest = Game1.player.questLog.FirstOrDefault(q => q.id.Value == questId);
                bool inJournal = quest != null;
                bool completed = quest?.completed.Value ?? false;
                bool inState = _stateService.HasActiveQuestState(questId);
                _monitor.Log($"  {questId}: inJournal={inJournal}, completed={completed}, modQuestState={inState}", LogLevel.Info);
            }

            var darknessQuestIds = new[]
            {
                "HarveyMod_DarknessTherapy",
                "HarveyMod_DarknessStep1",
                "HarveyMod_DarknessStep2",
                "HarveyMod_DarknessStep3",
            };

            foreach (var questId in darknessQuestIds)
            {
                var quest = Game1.player.questLog.FirstOrDefault(q => q.id.Value == questId);
                bool inJournal = quest != null;
                bool completed = quest?.completed.Value ?? false;
                _monitor.Log($"  {questId}: inJournal={inJournal}, completed={completed}", LogLevel.Info);
            }
        }

        private void WriteTopics()
        {
            _monitor.Log("\n--- TOPICS ---", LogLevel.Info);
            WriteTopicGroup("Stress topics", StressTopicIds);
            WriteTopicGroup("Treatment started topics", TreatmentStartedTopicIds);
            WriteTopicGroup("Cured topics", CuredTopicIds);
        }

        private void WriteTopicGroup(string label, IEnumerable<string> topicIds)
        {
            _monitor.Log($"{label}:", LogLevel.Info);
            int activeCount = 0;
            foreach (var topicId in topicIds)
            {
                if (!ConversationHelper.HasTopic(topicId))
                    continue;

                activeCount++;
                var days = Game1.player.activeDialogueEvents.TryGetValue(topicId, out int d) ? d : 0;
                _monitor.Log($"  • {topicId} = {days} days", LogLevel.Info);
            }

            if (activeCount == 0)
                _monitor.Log("  (none active)", LogLevel.Info);
        }

        private void WriteDarkness()
        {
            var d = _data.Darkness;
            _monitor.Log("\n--- DARKNESS ---", LogLevel.Info);
            _monitor.Log($"FearLevel: {d.FearLevel} ({d.GetFearLevelDescription()})", LogLevel.Info);
            _monitor.Log($"IsTherapyActive: {d.IsTherapyActive}", LogLevel.Info);
            _monitor.Log($"CurrentStep (TherapyStage): {d.TherapyStage}", LogLevel.Info);
            _monitor.Log($"SafeDarknessMinutes: {d.SafeDarknessMinutes}/15", LogLevel.Info);
            _monitor.Log($"SafeZonesVisited: [{string.Join(", ", d.SafeZonesVisited)}]", LogLevel.Info);
            _monitor.Log($"MountainNightSeconds: {d.MountainNightSeconds}/120", LogLevel.Info);
            _monitor.Log($"CompletedStep1/2/3: {d.CompletedStep1}/{d.CompletedStep2}/{d.CompletedStep3}", LogLevel.Info);
            _monitor.Log($"IsCured: {d.IsCured}, HasOvercomeBonus: {d.HasOvercomeBonus}", LogLevel.Info);
            _monitor.Log($"EpisodesThisWeek: {d.EpisodesThisWeek}, DaysIgnored: {d.DaysIgnored}", LogLevel.Info);
            _monitor.Log($"DarknessEpisodesCount: {d.DarknessEpisodesCount}", LogLevel.Info);
        }

        private void WriteProblems()
        {
            _monitor.Log("\n--- PROBLEMS (mod state vs game) ---", LogLevel.Info);
            var problems = CollectProblems();

            if (problems.Count == 0)
            {
                _monitor.Log("  ✅ No mismatches detected.", LogLevel.Info);
                return;
            }

            foreach (var problem in problems)
                _monitor.Log($"  ⚠ {problem}", LogLevel.Info);
        }

        private List<string> CollectProblems()
        {
            var problems = new List<string>();

            foreach (var treatment in _data.StressState.ActiveTreatments.Values)
            {
                if (treatment.IsCured || treatment.IsCompleted)
                    continue;

                if (!string.IsNullOrEmpty(treatment.BuffId)
                    && !_stateService.HasBuffInGame(treatment.BuffId))
                {
                    problems.Add(
                        $"State active ({treatment.TreatmentKey}, buff={treatment.BuffId}) but real buff missing on player");
                }

                if (treatment.TreatmentStarted && string.IsNullOrEmpty(treatment.QuestId))
                {
                    problems.Add(
                        $"TreatmentStarted=true but QuestId empty (TreatmentKey={treatment.TreatmentKey}, BuffId={treatment.BuffId})");
                }

                if (treatment.TreatmentStarted
                    && !treatment.IsCured
                    && !treatment.IsCompleted
                    && !string.IsNullOrEmpty(treatment.QuestId)
                    && _stateService.HasActiveQuestState(treatment.QuestId)
                    && !_stateService.HasQuestInGameJournal(treatment.QuestId))
                {
                    problems.Add(
                        $"Mod quest state active ({treatment.QuestId}) but quest missing from journal (TreatmentKey={treatment.TreatmentKey})");
                }
            }

            foreach (var (buffId, _) in CoreStressPairs)
            {
                if (_stateService.HasBuffInGame(buffId) && !_stateService.HasActiveTreatmentState(buffId))
                    problems.Add($"Real buff present ({buffId}) but no active mod treatment state");
            }

            foreach (var buffId in DarknessLevelBuffIds)
            {
                if (_stateService.HasBuffInGame(buffId) && !_stateService.HasActiveTreatmentState(BuffIds.Darkness))
                {
                    // Darkness levels use separate save path — only flag if no darkness-related state at all
                    if (_data.Darkness.FearLevel == 0 && !_data.Darkness.IsTherapyActive)
                        problems.Add($"Real darkness level buff present ({buffId}) but DarknessProgress.FearLevel=0");
                }
            }

            foreach (var (_, questId) in CoreStressPairs)
            {
                if (_stateService.HasQuestInGameJournal(questId) && !_stateService.HasActiveQuestState(questId))
                {
                    var quest = Game1.player.questLog.FirstOrDefault(q => q.id.Value == questId);
                    if (quest?.completed.Value != true)
                        problems.Add($"Journal has quest {questId} but mod quest state is not active");
                }
            }

            foreach (var (buffId, keys) in _data.StressState.ActiveTreatmentsByBuff)
            {
                foreach (var key in keys.ToList())
                {
                    if (!_data.StressState.ActiveTreatments.TryGetValue(key, out var treatment))
                    {
                        problems.Add($"ActiveTreatmentsByBuff[{buffId}] references missing TreatmentKey '{key}'");
                        continue;
                    }

                    if (!string.Equals(treatment.BuffId, buffId, StringComparison.Ordinal))
                    {
                        problems.Add(
                            $"ActiveTreatmentsByBuff index mismatch: key '{key}' indexed under '{buffId}' but treatment.BuffId='{treatment.BuffId}'");
                    }
                }
            }

            foreach (var (key, treatment) in _data.StressState.ActiveTreatments)
            {
                if (string.IsNullOrEmpty(treatment.BuffId))
                    continue;

                if (!_data.StressState.ActiveTreatmentsByBuff.TryGetValue(treatment.BuffId, out var keys)
                    || !keys.Contains(key))
                {
                    problems.Add(
                        $"ActiveTreatments entry '{key}' (BuffId={treatment.BuffId}) missing from ActiveTreatmentsByBuff index");
                }
            }

            return problems;
        }

        private static string FormatProgress(TreatmentProgress p)
        {
            var sb = new StringBuilder();
            sb.Append($"SecondsNearHarvey={p.SecondsNearHarvey}");
            sb.Append($", EveningInLightSeconds={p.EveningInLightSeconds}");
            sb.Append($", TalkedUniqueToday={p.TalkedUniqueToday}");
            sb.Append($", SocialTalksAfterQuest={p.SocialTalksAfterQuest}");
            sb.Append($", AteAnyFood={p.AteAnyFood}");
            sb.Append($", WarmSeconds={p.WarmSeconds}");
            sb.Append($", EarlySleepStreak={p.EarlySleepStreak}");
            sb.Append($", TiredRestSeconds={p.TiredRestSeconds}");
            if (p.TiredRestMinutes != 0)
                sb.Append($", TiredRestMinutes(legacy)={p.TiredRestMinutes}");
            if (p.TiredLastTimeOfDay.HasValue)
                sb.Append($", TiredLastTimeOfDay={p.TiredLastTimeOfDay}");
            return sb.ToString();
        }

        private static string NullOrValue(string? value)
            => string.IsNullOrEmpty(value) ? "(empty)" : value;
    }
}
