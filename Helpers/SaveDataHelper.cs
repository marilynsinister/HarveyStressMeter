using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using HarveyStressMeter.Models;

namespace HarveyStressMeter.Helpers
{
    /// <summary>
    /// Копирование и сброс SaveData in-place — сервисы держат ссылку на один экземпляр из ModEntry.
    /// </summary>
    public static class SaveDataHelper
    {
        public const string SaveKey = "stress-data-v1";
        private const string ModUniqueId = "marilynsinister.HarveyStressMeter";

        private static JsonSerializerSettings CreateSaveJsonSettings()
        {
            return new JsonSerializerSettings
            {
                Formatting = Formatting.None,
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                Converters =
                {
                    new SDateSaveDataConverter(),
                    new StringEnumConverter()
                }
            };
        }

        /// <summary>
        /// Читает mod save data с tolerant-десериализацией SDate (legacy Day=0).
        /// </summary>
        public static SaveData? ReadSaveData(IMonitor? monitor = null)
        {
            var rawJson = TryGetRawSaveJson();
            if (rawJson == null)
                return null;

            try
            {
                return JsonConvert.DeserializeObject<SaveData>(rawJson, CreateSaveJsonSettings());
            }
            catch (Exception ex)
            {
                monitor?.Log(
                    $"[SaveDataHelper] Failed to deserialize mod save '{SaveKey}': {ex.Message}. " +
                    "Starting with fresh mod state for this slot.",
                    LogLevel.Warn);
                return null;
            }
        }

        /// <summary>
        /// Записывает mod save data с тем же форматом SDate, что и <see cref="ReadSaveData"/>.
        /// </summary>
        public static void WriteSaveData(SaveData? data)
        {
            var internalKey = GetSaveFileKey(ModUniqueId, SaveKey);
            var json = data != null
                ? JsonConvert.SerializeObject(data, CreateSaveJsonSettings())
                : null;

            foreach (var dataField in GetSaveDataFields())
            {
                if (json != null)
                    dataField[internalKey] = json;
                else
                    dataField.Remove(internalKey);
            }
        }

        private static string? TryGetRawSaveJson()
        {
            var internalKey = GetSaveFileKey(ModUniqueId, SaveKey);

            foreach (var dataField in GetSaveDataFields())
            {
                if (dataField.TryGetValue(internalKey, out var value))
                    return value;
            }

            return null;
        }

        private static string GetSaveFileKey(string modId, string key)
        {
            return $"smapi/mod-data/{modId}/{key}".ToLowerInvariant();
        }

        private static IEnumerable<IDictionary<string, string>> GetSaveDataFields()
        {
            if (SaveGame.loaded != null)
            {
                yield return Game1.CustomData;
                yield return SaveGame.loaded.CustomData;
            }
            else
            {
                yield return Game1.CustomData;
            }
        }

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

            target.StressLoad ??= new StressLoadState();
            CopyStressLoadInto(target.StressLoad, source.StressLoad ?? new StressLoadState());

            target.ThunderFlashback ??= new ThunderFlashbackState();
            CopyThunderFlashbackInto(target.ThunderFlashback, source.ThunderFlashback ?? new ThunderFlashbackState());

            target.HarveyFlashbackRescue ??= new HarveyFlashbackRescueState();
            CopyHarveyFlashbackRescueInto(
                target.HarveyFlashbackRescue,
                source.HarveyFlashbackRescue ?? new HarveyFlashbackRescueState());

            target.HarveyCareTrust ??= new HarveyCareTrustState();
            CopyHarveyCareTrustInto(target.HarveyCareTrust, source.HarveyCareTrust ?? new HarveyCareTrustState());
            CopyHarveySafePersonAuraInto(
                target.HarveySafePersonAura,
                source.HarveySafePersonAura ?? new HarveySafePersonAuraState());

            target.ActiveTreatmentEpisode = CloneTreatmentEpisodeState(source.ActiveTreatmentEpisode);
            CopyEpisodeImmunityInto(target, source);

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
            target.StressLoad = new StressLoadState();
            target.ThunderFlashback = new ThunderFlashbackState();
            target.HarveyFlashbackRescue = new HarveyFlashbackRescueState();
            target.HarveyCareTrust = new HarveyCareTrustState();
            target.ActiveTreatmentEpisode = null;
            target.EpisodeImmunityUntil = new Dictionary<string, SDate>();

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

        private static void CopyStressLoadInto(StressLoadState target, StressLoadState source)
        {
            target.CurrentStressLoad = source.CurrentStressLoad;
            target.Severity = source.Severity;
            target.ActiveEpisodeId = source.ActiveEpisodeId;
            target.ActiveTreatmentEpisodeId = source.ActiveTreatmentEpisodeId;
            target.HasActiveTreatment = source.HasActiveTreatment;
            target.AwaitingHarveyReview = source.AwaitingHarveyReview;
            target.LastUpdatedTime = source.LastUpdatedTime;
            target.LastPrimaryCause = source.LastPrimaryCause;
            target.GotoroFlashbackActive = source.GotoroFlashbackActive;
            target.WarTraumaFlag = source.WarTraumaFlag;

            target.ActiveCauses.Clear();
            foreach (var (causeId, cause) in source.ActiveCauses)
            {
                target.ActiveCauses[causeId] = new StressCauseState
                {
                    CauseId = cause.CauseId,
                    SourceBuffId = cause.SourceBuffId,
                    Weight = cause.Weight,
                    IsActive = cause.IsActive,
                    IsSevere = cause.IsSevere,
                    AppliedTime = cause.AppliedTime,
                    LastUpdatedTime = cause.LastUpdatedTime,
                    CanSelfResolve = cause.CanSelfResolve,
                    RequiresHarveyIfSevere = cause.RequiresHarveyIfSevere,
                };
            }
        }

        private static void CopyThunderFlashbackInto(ThunderFlashbackState target, ThunderFlashbackState source)
        {
            target.IsActive = source.IsActive;
            target.WasTriggeredToday = source.WasTriggeredToday;
            target.WasStabilizedToday = source.WasStabilizedToday;
            target.EnteredForestDuringFlashback = source.EnteredForestDuringFlashback;
            target.ForestShelterSeconds = source.ForestShelterSeconds;
            target.RequiredForestShelterSeconds = source.RequiredForestShelterSeconds;
            target.TriggerLocation = source.TriggerLocation;
            target.TriggerTime = source.TriggerTime;
            target.LastFrightCheckTime = source.LastFrightCheckTime;
            target.LastHudMessageTime = source.LastHudMessageTime;
            target.HudMessageCooldownMinutes = source.HudMessageCooldownMinutes;
            target.LightningFrightIntensity = source.LightningFrightIntensity;
            target.IsGotoroFlashback = source.IsGotoroFlashback;
        }

        private static void CopyHarveyCareTrustInto(HarveyCareTrustState target, HarveyCareTrustState source)
        {
            target.TrustPoints = source.TrustPoints;
            target.TrustLevel = source.TrustLevel;
            target.SuccessfulAssignments = source.SuccessfulAssignments;
            target.IgnoredAssignments = source.IgnoredAssignments;
            target.FlashbacksStabilizedWithHarvey = source.FlashbacksStabilizedWithHarvey;
            target.DaysSinceLastSuccessfulAssignment = source.DaysSinceLastSuccessfulAssignment;
            target.LastTrustGainDay = source.LastTrustGainDay;
            target.LastTrustPenaltyDay = source.LastTrustPenaltyDay;
            target.ReviewOfferedAbsoluteDay = source.ReviewOfferedAbsoluteDay;
            target.AssignmentBoostDaysRemaining = source.AssignmentBoostDaysRemaining;
            target.SafePersonUnlocked = source.SafePersonUnlocked;
            target.ForestRescueUnlocked = source.ForestRescueUnlocked;
            target.GroundingDialogueUnlocked = source.GroundingDialogueUnlocked;
            target.LastAmbientTrustEpisodeId = source.LastAmbientTrustEpisodeId;
            target.SupportiveTalkTrustToday = source.SupportiveTalkTrustToday;
            target.ShownCareTrustDialogueKeys = new HashSet<string>(source.ShownCareTrustDialogueKeys ?? new HashSet<string>());
        }

        private static void CopyHarveySafePersonAuraInto(
            HarveySafePersonAuraState target,
            HarveySafePersonAuraState source)
        {
            target.LastProcessTime = source.LastProcessTime;
            target.LastMessageTime = source.LastMessageTime;
            target.LastDecayAmount = source.LastDecayAmount;
            target.LastDistanceTiles = source.LastDistanceTiles;
            target.LastHarveyNearby = source.LastHarveyNearby;
            target.LastSafeAuraActive = source.LastSafeAuraActive;
        }

        private static void CopyHarveyFlashbackRescueInto(
            HarveyFlashbackRescueState target,
            HarveyFlashbackRescueState source)
        {
            target.HarveyRescueTriggeredToday = source.HarveyRescueTriggeredToday;
            target.LastRescueDay = source.LastRescueDay;
            target.LastRescueEventId = source.LastRescueEventId;
            target.ForestSecondsBeforeRescue = source.ForestSecondsBeforeRescue;
            target.HarveyHelpedStabilizeToday = source.HarveyHelpedStabilizeToday;
            target.LastRescueCheckTime = source.LastRescueCheckTime;
            target.RescueTier = source.RescueTier;
            target.PendingPostRescueTier = source.PendingPostRescueTier;
            target.PendingPostRescueEventId = source.PendingPostRescueEventId;
        }

        private static TreatmentEpisodeState? CloneTreatmentEpisodeState(TreatmentEpisodeState? source)
        {
            if (source == null)
                return null;

            return new TreatmentEpisodeState
            {
                EpisodeId = source.EpisodeId,
                RelatedCauseIds = new List<string>(source.RelatedCauseIds),
                QuestId = source.QuestId,
                TreatmentStarted = source.TreatmentStarted,
                ObjectivesCompleted = source.ObjectivesCompleted,
                AwaitingHarveyReview = source.AwaitingHarveyReview,
                IsCompleted = source.IsCompleted,
                IsCured = source.IsCured,
                StartedTime = source.StartedTime,
                ReadyForReviewTime = source.ReadyForReviewTime,
                PrimaryCauseId = source.PrimaryCauseId,
            };
        }

        private static void CopyEpisodeImmunityInto(SaveData target, SaveData source)
        {
            target.EpisodeImmunityUntil ??= new Dictionary<string, SDate>();
            target.EpisodeImmunityUntil.Clear();
            if (source.EpisodeImmunityUntil == null)
                return;

            foreach (var (episodeId, until) in source.EpisodeImmunityUntil)
                target.EpisodeImmunityUntil[episodeId] = until;
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
                ObjectivesCompleted = source.ObjectivesCompleted,
                AwaitingHarveyReview = source.AwaitingHarveyReview,
                ReadyForReviewDate = source.ReadyForReviewDate,
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
