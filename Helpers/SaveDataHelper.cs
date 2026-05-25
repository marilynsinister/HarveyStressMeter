using System;
using System.Collections.Generic;
using HarveyStressMeter.Models;

namespace HarveyStressMeter.Helpers
{
    /// <summary>
    /// Копирование и сброс SaveData in-place — сервисы держат ссылку на один экземпляр из ModEntry.
    /// </summary>
    public static class SaveDataHelper
    {
        public const string SaveKey = "stress-data-v1";

        /// <summary>
        /// Копирует все поля из <paramref name="source"/> в существующий <paramref name="target"/>.
        /// Ссылка <paramref name="target"/> не меняется.
        /// </summary>
        public static void CopySaveDataIntoExistingInstance(SaveData target, SaveData source)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            target.LastDay = source.LastDay;

            target.TalkedNpcsToday ??= new HashSet<string>();
            target.TalkedNpcsToday.Clear();
            if (source.TalkedNpcsToday != null)
            {
                foreach (var npc in source.TalkedNpcsToday)
                    target.TalkedNpcsToday.Add(npc);
            }

            target.OverworkBreaksToday = source.OverworkBreaksToday;
            target.OverworkBreakSeconds = source.OverworkBreakSeconds;
            target.OverworkBreakActive = source.OverworkBreakActive;
            target.TalkedToHarveyToday = source.TalkedToHarveyToday;
            target.DaysWithoutTalking = source.DaysWithoutTalking;
            target.DaysWithoutEating = source.DaysWithoutEating;
            target.DaysWithLateSleep = source.DaysWithLateSleep;

            target.StressState ??= new PlayerStressState();
            CopyStressStateInto(target.StressState, source.StressState ?? new PlayerStressState());

            target.Darkness ??= new DarknessProgress();
            CopyDarknessInto(target.Darkness, source.Darkness ?? new DarknessProgress());

#pragma warning disable CS0618
            CopyDictionary(source.ActiveLockedDebuffs, target.ActiveLockedDebuffs);
            CopyTreatmentProgressDictionary(source.Treatment, target.Treatment);
            CopyDictionary(source.LastIssuedDay, target.LastIssuedDay);
            CopyStressBuffStatesDictionary(source.StressBuffStates, target.StressBuffStates);
#pragma warning restore CS0618
        }

        /// <summary>
        /// Сбрасывает поля существующего SaveData к значениям по умолчанию (новый слот / нет mod save).
        /// </summary>
        public static void ResetSaveDataInPlace(SaveData target)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            target.LastDay = StardewModdingAPI.Utilities.SDate.Now();
            target.TalkedNpcsToday = new HashSet<string>();
            target.OverworkBreaksToday = 0;
            target.OverworkBreakSeconds = 0;
            target.OverworkBreakActive = false;
            target.TalkedToHarveyToday = false;
            target.DaysWithoutTalking = 0;
            target.DaysWithoutEating = 0;
            target.DaysWithLateSleep = 0;
            target.StressState = new PlayerStressState();
            target.Darkness = new DarknessProgress();

#pragma warning disable CS0618
            target.ActiveLockedDebuffs = new Dictionary<string, string>();
            target.Treatment = new Dictionary<string, TreatmentProgress>();
            target.LastIssuedDay = new Dictionary<string, StardewModdingAPI.Utilities.SDate>();
            target.StressBuffStates = new Dictionary<string, List<TreatmentState>>();
#pragma warning restore CS0618
        }

        private static void CopyStressStateInto(PlayerStressState target, PlayerStressState source)
        {
            target.CopyActiveTreatmentsFrom(source, CloneTreatmentState);

            target.TreatmentHistory.Clear();
            foreach (var (buffId, treatments) in source.TreatmentHistory)
            {
                var cloned = new List<TreatmentState>(treatments.Count);
                foreach (var t in treatments)
                    cloned.Add(CloneTreatmentState(t));
                target.TreatmentHistory[buffId] = cloned;
            }

            target.LastIssuedDay.Clear();
            foreach (var (buffId, date) in source.LastIssuedDay)
                target.LastIssuedDay[buffId] = date;

            target.LastTreatmentOfferDateByBuff.Clear();
            foreach (var (buffId, date) in source.LastTreatmentOfferDateByBuff)
                target.LastTreatmentOfferDateByBuff[buffId] = date;

            target.LastTreatmentDeclinedDateByBuff.Clear();
            foreach (var (buffId, date) in source.LastTreatmentDeclinedDateByBuff)
                target.LastTreatmentDeclinedDateByBuff[buffId] = date;

            CopyTreatmentFlagsInto(target.TreatmentFlags, source.TreatmentFlags);

            target.DebuffImmunities.Clear();
            foreach (var (buffId, date) in source.DebuffImmunities)
                target.DebuffImmunities[buffId] = date;
        }

        private static void CopyTreatmentFlagsInto(TreatmentFlags target, TreatmentFlags source)
        {
            target.ActiveTreatmentFlags.Clear();
            foreach (var (key, value) in source.ActiveTreatmentFlags)
                target.ActiveTreatmentFlags[key] = value;

            target.QuestAddedToJournalFlags.Clear();
            foreach (var (key, value) in source.QuestAddedToJournalFlags)
                target.QuestAddedToJournalFlags[key] = value;

            target.LastProgressUpdate.Clear();
            foreach (var (key, value) in source.LastProgressUpdate)
                target.LastProgressUpdate[key] = value;

            target.UpdateSkipCounters.Clear();
            foreach (var (key, value) in source.UpdateSkipCounters)
                target.UpdateSkipCounters[key] = value;
        }

        private static void CopyDarknessInto(DarknessProgress target, DarknessProgress source)
        {
            target.FearLevel = source.FearLevel;
            target.DarknessEpisodesCount = source.DarknessEpisodesCount;
            target.LastEpisodeDate = source.LastEpisodeDate;
            target.EpisodesThisWeek = source.EpisodesThisWeek;
            target.WeekStartDate = source.WeekStartDate;
            target.IgnoredSinceDate = source.IgnoredSinceDate;
            target.DaysIgnored = source.DaysIgnored;
            target.LastLevelIncreaseDate = source.LastLevelIncreaseDate;
            target.SafeDarknessMinutes = source.SafeDarknessMinutes;
            target.SafeZonesVisited.Clear();
            if (source.SafeZonesVisited != null)
                target.SafeZonesVisited.AddRange(source.SafeZonesVisited);
            target.MountainNightSeconds = source.MountainNightSeconds;
            target.TherapyStage = source.TherapyStage;
            target.IsTherapyActive = source.IsTherapyActive;
            target.IsCured = source.IsCured;
            target.HasOvercomeBonus = source.HasOvercomeBonus;
            target.TherapyStartDate = source.TherapyStartDate;
            target.TherapyCompletedDate = source.TherapyCompletedDate;
            target.HasReceivedDimmer = source.HasReceivedDimmer;
            target.HasReceivedLantern = source.HasReceivedLantern;
            target.HasHarveySupportBuff = source.HasHarveySupportBuff;
            target.CompletedStep1 = source.CompletedStep1;
            target.CompletedStep2 = source.CompletedStep2;
            target.CompletedStep3 = source.CompletedStep3;
        }

        private static TreatmentState CloneTreatmentState(TreatmentState source)
        {
            return new TreatmentState
            {
                BuffId = source.BuffId,
                QuestId = source.QuestId,
                TreatmentKey = source.TreatmentKey,
                InstanceNumber = source.InstanceNumber,
                IssuedDate = source.IssuedDate,
                TreatmentStartedDate = source.TreatmentStartedDate,
                CompletedDate = source.CompletedDate,
                TreatmentStarted = source.TreatmentStarted,
                AddedToGameLog = source.AddedToGameLog,
                IsCured = source.IsCured,
                IsCompleted = source.IsCompleted,
                Progress = CloneTreatmentProgress(source.Progress)
            };
        }

        private static TreatmentProgress CloneTreatmentProgress(TreatmentProgress? source)
        {
            if (source == null)
                return new TreatmentProgress();

            return new TreatmentProgress
            {
                QuestId = source.QuestId,
                StartedOn = source.StartedOn,
                SecondsNearHarvey = source.SecondsNearHarvey,
                EveningInLightSeconds = source.EveningInLightSeconds,
                TalkedUniqueToday = source.TalkedUniqueToday,
                SocialTalksAfterQuest = source.SocialTalksAfterQuest,
                AteAnyFood = source.AteAnyFood,
                WarmSeconds = source.WarmSeconds,
                EarlySleepStreak = source.EarlySleepStreak,
                TiredRestSeconds = source.TiredRestSeconds,
                TiredRestMinutes = source.TiredRestMinutes,
                TiredLastTimeOfDay = source.TiredLastTimeOfDay
            };
        }

        private static void CopyDictionary<TKey, TValue>(
            Dictionary<TKey, TValue>? source,
            Dictionary<TKey, TValue> target)
            where TKey : notnull
        {
            target.Clear();
            if (source == null)
                return;

            foreach (var (key, value) in source)
                target[key] = value;
        }

        private static void CopyTreatmentProgressDictionary(
            Dictionary<string, TreatmentProgress>? source,
            Dictionary<string, TreatmentProgress> target)
        {
            target.Clear();
            if (source == null)
                return;

            foreach (var (key, progress) in source)
                target[key] = CloneTreatmentProgress(progress);
        }

        private static void CopyStressBuffStatesDictionary(
            Dictionary<string, List<TreatmentState>>? source,
            Dictionary<string, List<TreatmentState>> target)
        {
            target.Clear();
            if (source == null)
                return;

            foreach (var (buffId, treatments) in source)
            {
                var cloned = new List<TreatmentState>(treatments.Count);
                foreach (var t in treatments)
                    cloned.Add(CloneTreatmentState(t));
                target[buffId] = cloned;
            }
        }
    }
}
