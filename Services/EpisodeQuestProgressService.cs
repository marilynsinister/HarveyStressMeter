using System;
using System.Linq;
using System.Text;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;
using StardewModdingAPI;
using StardewValley;

namespace HarveyStressMeter.Services
{
    /// <summary>Трекинг прогресса episode-квестов (PhysicalExhaustion, Burnout, AnxietySpike, Gotoro).</summary>
    public sealed class EpisodeQuestProgressService
    {
        private readonly SaveData _data;
        private readonly QuestService _questService;
        private readonly TreatmentService _treatmentService;
        private readonly IMonitor _monitor;
        private RecoveryPlanBridge? _recoveryPlanBridge;

        private bool? _anxietyLastSafe;
        private int _anxietyLastJournalSeconds = -1;
        private int _anxietyLastLoggedSeconds = -1;

        public EpisodeQuestProgressService(
            SaveData data,
            QuestService questService,
            TreatmentService treatmentService,
            IMonitor monitor)
        {
            _data = data;
            _questService = questService;
            _treatmentService = treatmentService;
            _monitor = monitor;
        }

        public void SetRecoveryPlanBridge(RecoveryPlanBridge bridge)
            => _recoveryPlanBridge = bridge;

        public void InitializeEpisodeQuest(string episodeId, TreatmentProgress progress)
        {
            progress.EpisodeCausesCompleted.Clear();
            progress.AnxietySafeSeconds = 0;
            progress.AnxietySpikeCompletionAnnounced = false;
            progress.BurnoutAvoidedMinesToday = true;
            progress.WarmSeconds = 0;
            progress.TiredRestSeconds = 0;
            progress.SecondsNearHarvey = 0;

            if (episodeId == StressEpisodes.GotoroFlashback
                && _data.ThunderFlashback.DeferredGotoroShelterSeconds > 0)
            {
                progress.SecondsNearHarvey = _data.ThunderFlashback.DeferredGotoroShelterSeconds;
                _data.ThunderFlashback.DeferredGotoroShelterSeconds = 0;
                _monitor.Log(
                    $"[EpisodeQuest] Gotoro: перенесено отложенное укрытие {progress.SecondsNearHarvey} сек",
                    LogLevel.Info);
            }

            UpdateQuestJournal(episodeId, progress);
            ResetAnxietyTracking();
            _recoveryPlanBridge?.StartEpisodePlan(episodeId);
            if (TreatmentEpisodeDefinitions.TryGet(episodeId, out var definition))
            {
                _questService.UpdateQuest(
                    definition.QuestId,
                    description: "План Харви активен. Подробности — клавиша H.");
            }
        }

        public void UpdateActiveEpisode(bool harveyNearby)
        {
            // AnxietySpike может идти параллельно другому episode (например SocialShutdown).
            UpdateAnxietySpikeProgress();

            var episode = _data.ActiveTreatmentEpisode;
            if (episode == null || !episode.IsActiveEpisode() || episode.AwaitingHarveyReview)
                return;

            var treatment = _data.StressState.GetActiveTreatmentByQuest(episode.QuestId);
            if (treatment?.Progress == null)
                return;

            var progress = treatment.Progress;

            switch (episode.EpisodeId)
            {
                case StressEpisodes.PhysicalExhaustion:
                    TickPhysicalExhaustion(episode, progress);
                    break;
                case StressEpisodes.Burnout:
                    UpdateQuestJournal(StressEpisodes.Burnout, progress);
                    break;
                case StressEpisodes.GotoroFlashback:
                    TickGotoroFlashback(progress);
                    break;
            }
        }

        public void OnPlayerWarped(string? locationName)
        {
            if (_data.StressState.GetActiveTreatmentByQuest(QuestIds.AnxietySpike)?.IsTreatmentActive() == true)
                UpdateAnxietySpikeProgress(allowIncrement: false);

            var episode = _data.ActiveTreatmentEpisode;
            if (episode == null || !episode.IsActiveEpisode() || episode.AwaitingHarveyReview)
                return;

            if (episode.EpisodeId != StressEpisodes.Burnout)
                return;

            if (!GameStateHelper.IsStressfulWorkLocation(locationName))
                return;

            var treatment = _data.StressState.GetActiveTreatmentByQuest(episode.QuestId);
            if (treatment?.Progress == null || !treatment.Progress.BurnoutAvoidedMinesToday)
                return;

            treatment.Progress.BurnoutAvoidedMinesToday = false;
            Game1.addHUDMessage(new HUDMessage(
                "⚠️ Назначение: сегодня вы в шахте — нужен день без шахт",
                HUDMessage.error_type));
            UpdateQuestJournal(StressEpisodes.Burnout, treatment.Progress);
        }

        public void OnFoodConsumed()
        {
            var episode = _data.ActiveTreatmentEpisode;
            if (episode?.EpisodeId != StressEpisodes.PhysicalExhaustion || episode.AwaitingHarveyReview)
                return;

            if (!episode.RelatedCauseIds.Contains(StressCauses.Hunger, StringComparer.Ordinal))
                return;

            var treatment = _data.StressState.GetActiveTreatmentByQuest(episode.QuestId);
            if (treatment?.Progress == null)
                return;

            treatment.Progress.AteAnyFood = true;
            MarkCauseCompleted(treatment.Progress, StressCauses.Hunger);
            UpdateQuestJournal(StressEpisodes.PhysicalExhaustion, treatment.Progress);
            TryCompleteEpisode(StressEpisodes.PhysicalExhaustion, treatment.Progress);
        }

        public void OnHotDrinkConsumed()
        {
            var episode = _data.ActiveTreatmentEpisode;
            if (episode?.EpisodeId != StressEpisodes.PhysicalExhaustion || episode.AwaitingHarveyReview)
                return;

            if (!episode.RelatedCauseIds.Contains(StressCauses.TooCold, StringComparer.Ordinal))
                return;

            var treatment = _data.StressState.GetActiveTreatmentByQuest(episode.QuestId);
            if (treatment?.Progress == null)
                return;

            treatment.Progress.WarmSeconds = Math.Max(
                treatment.Progress.WarmSeconds,
                EpisodeQuestRules.PhysicalWarmSecondsRequired);
            MarkCauseCompleted(treatment.Progress, StressCauses.TooCold);
            UpdateQuestJournal(StressEpisodes.PhysicalExhaustion, treatment.Progress);
            TryCompleteEpisode(StressEpisodes.PhysicalExhaustion, treatment.Progress);
        }

        public void OnDayEnding(int timeOfDay)
        {
            var episode = _data.ActiveTreatmentEpisode;
            if (episode == null || !episode.IsActiveEpisode() || episode.AwaitingHarveyReview)
                return;

            var treatment = _data.StressState.GetActiveTreatmentByQuest(episode.QuestId);
            if (treatment?.Progress == null)
                return;

            var earlySleep = timeOfDay >= 600 && timeOfDay <= EpisodeQuestRules.EarlySleepLatestTime;

            switch (episode.EpisodeId)
            {
                case StressEpisodes.PhysicalExhaustion:
                    if (earlySleep && episode.RelatedCauseIds.Contains(StressCauses.NoSleep, StringComparer.Ordinal))
                        MarkCauseCompleted(treatment.Progress, StressCauses.NoSleep);
                    UpdateQuestJournal(StressEpisodes.PhysicalExhaustion, treatment.Progress);
                    TryCompleteEpisode(StressEpisodes.PhysicalExhaustion, treatment.Progress);
                    break;

                case StressEpisodes.Burnout:
                    if (earlySleep && treatment.Progress.BurnoutAvoidedMinesToday)
                        TryCompleteEpisode(StressEpisodes.Burnout, treatment.Progress);
                    else
                        UpdateQuestJournal(StressEpisodes.Burnout, treatment.Progress);
                    break;
            }
        }

        public void OnFlashbackShelterUpdated(int shelterSeconds, string episodeId)
        {
            if (string.Equals(episodeId, StressEpisodes.AnxietySpike, StringComparison.Ordinal))
            {
                if (TryGetActiveAnxietySpikeTreatment() != null)
                    SetAnxietySafeSeconds(shelterSeconds);
                return;
            }

            var episode = _data.ActiveTreatmentEpisode;
            if (episode == null
                || !string.Equals(episode.EpisodeId, episodeId, StringComparison.Ordinal)
                || episode.AwaitingHarveyReview)
            {
                return;
            }

            var treatment = _data.StressState.GetActiveTreatmentByQuest(episode.QuestId);
            if (treatment?.Progress == null)
                return;

            if (episodeId == StressEpisodes.GotoroFlashback)
            {
                treatment.Progress.SecondsNearHarvey = Math.Max(
                    treatment.Progress.SecondsNearHarvey,
                    shelterSeconds);
                UpdateQuestJournal(StressEpisodes.GotoroFlashback, treatment.Progress);
            }
        }

        public bool ShouldMarkReadyForReviewOnFlashbackStabilization(string? activeEpisodeId)
            => activeEpisodeId is StressEpisodes.GotoroFlashback or StressEpisodes.AnxietySpike;

        private void TickPhysicalExhaustion(TreatmentEpisodeState episode, TreatmentProgress progress)
        {
            if (Game1.CurrentEvent != null)
                return;

            if (episode.RelatedCauseIds.Contains(StressCauses.TooCold, StringComparer.Ordinal)
                && GameStateHelper.IsInWarmZone())
            {
                progress.WarmSeconds++;
            }

            if (episode.RelatedCauseIds.Contains(StressCauses.Tired, StringComparer.Ordinal)
                && Game1.player.currentLocation is StardewValley.Locations.FarmHouse
                && !GameStateHelper.HasHeavyTools(Game1.player))
            {
                progress.TiredRestSeconds++;
            }

            foreach (var causeId in episode.RelatedCauseIds)
            {
                if (progress.EpisodeCausesCompleted.Contains(causeId))
                    continue;

                if (IsPhysicalCauseMet(causeId, progress))
                    MarkCauseCompleted(progress, causeId);
            }

            UpdateQuestJournal(StressEpisodes.PhysicalExhaustion, progress);
            TryCompleteEpisode(StressEpisodes.PhysicalExhaustion, progress);
        }

        private bool IsPhysicalCauseMet(string causeId, TreatmentProgress progress) => causeId switch
        {
            StressCauses.Hunger =>
                progress.AteAnyFood || ConversationHelper.HasTopic(TopicIds.AteToday),

            StressCauses.TooCold =>
                progress.WarmSeconds >= EpisodeQuestRules.PhysicalWarmSecondsRequired,

            StressCauses.Tired =>
                progress.TiredRestSeconds >= EpisodeQuestRules.PhysicalTiredRestSecondsRequired,

            StressCauses.Overwork =>
                _data.OverworkBreaksToday >= EpisodeQuestRules.PhysicalOverworkBreaksRequired,

            StressCauses.NoSleep =>
                progress.EpisodeCausesCompleted.Contains(StressCauses.NoSleep),

            _ => false,
        };

        /// <summary>
        /// Единый тик прогресса AnxietySpike: счётчик, journal, HUD и ready-for-review.
        /// Источник правды — <see cref="TreatmentProgress.AnxietySafeSeconds"/>.
        /// Работает и при параллельном другом ActiveTreatmentEpisode (например SocialShutdown).
        /// </summary>
        public void UpdateAnxietySpikeProgress(bool allowIncrement = true)
        {
            var treatment = TryGetActiveAnxietySpikeTreatment();
            if (treatment?.Progress == null)
                return;

            var progress = treatment.Progress;
            if (!progress.CanEvaluateQuestObjectives() || Game1.CurrentEvent != null)
                return;

            var location = Game1.currentLocation?.Name ?? "(null)";
            var isSafe = GameStateHelper.IsAnxietySafeLocation();

            if (allowIncrement && isSafe)
                progress.AnxietySafeSeconds++;

            _recoveryPlanBridge?.SyncProgress(
                StressEpisodes.AnxietySpike,
                progress.AnxietySafeSeconds,
                EpisodeQuestRules.AnxietySafeSecondsRequired);

            LogAnxietySpikeLocation(location, isSafe, progress.AnxietySafeSeconds);

            if (ShouldUpdateAnxietyJournal(progress, isSafe))
            {
                _anxietyLastSafe = isSafe;
                _anxietyLastJournalSeconds = progress.AnxietySafeSeconds;
                UpdateQuestJournal(StressEpisodes.AnxietySpike, progress);
            }

            if (allowIncrement && isSafe)
                ShowAnxietyHudMilestones(progress);

            TryCompleteAnxietySpike(treatment, progress);
        }

        private TreatmentState? TryGetActiveAnxietySpikeTreatment()
        {
            var treatment = _data.StressState.GetActiveTreatmentByQuest(QuestIds.AnxietySpike);
            if (treatment?.Progress == null || !treatment.IsTreatmentActive())
                return null;

            if (treatment.IsCompleted || treatment.IsCured || treatment.AwaitingHarveyReview)
                return null;

            return treatment;
        }

        /// <summary>Синхронизирует секунды укрытия (flashback) с единым прогрессом AnxietySpike.</summary>
        public void SetAnxietySafeSeconds(int seconds)
        {
            var treatment = TryGetActiveAnxietySpikeTreatment();
            if (treatment?.Progress == null)
                return;

            var progress = treatment.Progress;
            var previous = progress.AnxietySafeSeconds;
            progress.AnxietySafeSeconds = Math.Max(previous, seconds);
            if (progress.AnxietySafeSeconds == previous)
                return;

            _monitor.Log(
                $"[AnxietySpike] Shelter sync: {previous} → {progress.AnxietySafeSeconds}/" +
                $"{EpisodeQuestRules.AnxietySafeSecondsRequired}",
                LogLevel.Info);

            _anxietyLastJournalSeconds = progress.AnxietySafeSeconds;
            UpdateQuestJournal(StressEpisodes.AnxietySpike, progress);
            ShowAnxietyHudMilestones(progress, includeSkipped: true);
            TryCompleteAnxietySpike(treatment, progress);
        }

        /// <summary>Восстанавливает зависший AnxietySpike: objectives выполнены, но review не выставлен.</summary>
        public bool RepairStuckAnxietySpike(string? hudMessage = null)
        {
            var treatment = _data.StressState.GetActiveTreatmentByQuest(QuestIds.AnxietySpike);
            if (treatment?.Progress == null || !treatment.IsTreatmentActive())
                return false;

            RestoreAnxietyEpisodeStateIfMissing(treatment);

            if (treatment.AwaitingHarveyReview)
                return false;

            if (treatment.Progress.AnxietySafeSeconds < EpisodeQuestRules.AnxietySafeSecondsRequired)
                return false;

            treatment.Progress.AnxietySpikeCompletionAnnounced = true;
            _anxietyLastJournalSeconds = treatment.Progress.AnxietySafeSeconds;
            UpdateQuestJournal(StressEpisodes.AnxietySpike, treatment.Progress);

            _treatmentService.MarkTreatmentReadyForReviewByEpisode(
                StressEpisodes.AnxietySpike,
                hudMessage ?? "Назначение восстановлено: поговорите с Харви.");

            _recoveryPlanBridge?.CompleteEpisodeAssignment(StressEpisodes.AnxietySpike);

            _monitor.Log("[AnxietySpike] Repaired stuck state → AwaitingHarveyReview.", LogLevel.Warn);
            return true;
        }

        private void RestoreAnxietyEpisodeStateIfMissing(TreatmentState treatment)
        {
            var episode = _data.ActiveTreatmentEpisode;
            if (episode != null
                && episode.IsActiveEpisode()
                && !string.Equals(episode.EpisodeId, StressEpisodes.AnxietySpike, StringComparison.Ordinal))
            {
                return;
            }

            if (episode != null
                && string.Equals(episode.EpisodeId, StressEpisodes.AnxietySpike, StringComparison.Ordinal)
                && episode.IsActiveEpisode())
            {
                return;
            }

            if (!TreatmentEpisodeDefinitions.TryGet(StressEpisodes.AnxietySpike, out var definition))
                return;

            _data.ActiveTreatmentEpisode = new TreatmentEpisodeState
            {
                EpisodeId = StressEpisodes.AnxietySpike,
                QuestId = QuestIds.AnxietySpike,
                TreatmentStarted = true,
                AwaitingHarveyReview = treatment.AwaitingHarveyReview,
                ObjectivesCompleted = treatment.ObjectivesCompleted
                    || treatment.Progress.AnxietySafeSeconds >= EpisodeQuestRules.AnxietySafeSecondsRequired,
                RelatedCauseIds = definition.RelatedCauses.ToList(),
            };

            _monitor.Log(
                "[AnxietySpike] Restored missing ActiveTreatmentEpisode from active AnxietySpike quest.",
                LogLevel.Warn);
        }

        public void ResetAnxietyTracking()
        {
            _anxietyLastSafe = null;
            _anxietyLastJournalSeconds = -1;
            _anxietyLastLoggedSeconds = -1;
        }

        private void LogAnxietySpikeLocation(string location, bool isSafe, int seconds)
        {
            bool milestone = seconds is 0 or 30 or 60 or EpisodeQuestRules.AnxietySafeSecondsRequired;
            bool safeChanged = _anxietyLastSafe != isSafe;
            if (!milestone && !safeChanged && seconds - _anxietyLastLoggedSeconds < 30)
                return;

            _anxietyLastLoggedSeconds = seconds;
            _monitor.Log(
                $"[AnxietySpike] location={location}, safe={isSafe.ToString().ToLowerInvariant()}, seconds={seconds}",
                LogLevel.Info);
        }

        private bool ShouldUpdateAnxietyJournal(TreatmentProgress progress, bool isSafe)
        {
            var seconds = progress.AnxietySafeSeconds;

            if (_anxietyLastSafe != isSafe)
                return true;

            if (seconds != _anxietyLastJournalSeconds)
            {
                if (seconds is 0 or 30 or 60 or EpisodeQuestRules.AnxietySafeSecondsRequired)
                    return true;

                if (_anxietyLastJournalSeconds < 0)
                    return true;

                if (seconds - _anxietyLastJournalSeconds >= 5)
                    return true;
            }

            return false;
        }

        private void ShowAnxietyHudMilestones(TreatmentProgress progress, bool includeSkipped = false)
        {
            var seconds = progress.AnxietySafeSeconds;
            var required = EpisodeQuestRules.AnxietySafeSecondsRequired;

            if (includeSkipped)
            {
                if (seconds >= required)
                {
                    // Completion HUD handled by TryCompleteAnxietySpike.
                    return;
                }

                if (seconds >= 60)
                {
                    Game1.addHUDMessage(new HUDMessage(
                        $"Безопасное место: {Math.Min(seconds, required)}/{required} сек",
                        HUDMessage.newQuest_type));
                }
                else if (seconds >= 30)
                {
                    Game1.addHUDMessage(new HUDMessage(
                        $"Безопасное место: {seconds}/{required} сек",
                        HUDMessage.newQuest_type));
                }

                return;
            }

            if (seconds is 30 or 60)
            {
                Game1.addHUDMessage(new HUDMessage(
                    $"Безопасное место: {seconds}/{required} сек",
                    HUDMessage.newQuest_type));
            }
        }

        private void TryCompleteAnxietySpike(TreatmentState treatment, TreatmentProgress progress)
        {
            if (progress.AnxietySafeSeconds < EpisodeQuestRules.AnxietySafeSecondsRequired)
                return;

            if (progress.AnxietySpikeCompletionAnnounced || treatment.AwaitingHarveyReview)
                return;

            progress.AnxietySpikeCompletionAnnounced = true;
            _anxietyLastJournalSeconds = progress.AnxietySafeSeconds;

            _monitor.Log(
                $"[AnxietySpike] Objectives met ({progress.AnxietySafeSeconds}/" +
                $"{EpisodeQuestRules.AnxietySafeSecondsRequired}). Marking ready for Harvey review " +
                $"(parallel episode={_data.ActiveTreatmentEpisode?.EpisodeId ?? "(none)"}).",
                LogLevel.Info);

            Game1.playSound("questcomplete");
            Game1.addHUDMessage(new HUDMessage(
                $"✅ Безопасное место: {EpisodeQuestRules.AnxietySafeSecondsRequired}/" +
                $"{EpisodeQuestRules.AnxietySafeSecondsRequired} сек. Теперь поговорите с Харви.",
                HUDMessage.achievement_type));

            UpdateQuestJournal(StressEpisodes.AnxietySpike, progress);
            _treatmentService.MarkTreatmentReadyForReviewByEpisode(StressEpisodes.AnxietySpike);
            _recoveryPlanBridge?.CompleteEpisodeAssignment(StressEpisodes.AnxietySpike);
        }

        private void TickGotoroFlashback(TreatmentProgress progress)
        {
            progress.SecondsNearHarvey = Math.Max(
                progress.SecondsNearHarvey,
                _data.ThunderFlashback.ForestShelterSeconds);
            UpdateQuestJournal(StressEpisodes.GotoroFlashback, progress);
        }

        private void MarkCauseCompleted(TreatmentProgress progress, string causeId)
        {
            if (!progress.EpisodeCausesCompleted.Add(causeId))
                return;

            _monitor.Log($"[EpisodeQuest] PhysicalExhaustion: выполнено — {causeId}", LogLevel.Debug);
        }

        private void TryCompleteEpisode(string episodeId, TreatmentProgress progress)
        {
            if (episodeId == StressEpisodes.AnxietySpike)
            {
                var anxietyTreatment = _data.StressState.GetActiveTreatmentByQuest(QuestIds.AnxietySpike);
                if (anxietyTreatment?.Progress != null)
                    TryCompleteAnxietySpike(anxietyTreatment, anxietyTreatment.Progress);
                return;
            }

            if (!IsEpisodeObjectivesMet(episodeId, progress))
                return;

            var episode = _data.ActiveTreatmentEpisode;
            if (episode == null || episode.AwaitingHarveyReview)
                return;

            _monitor.Log($"[EpisodeQuest] Цели выполнены: {episodeId}", LogLevel.Info);
            Game1.playSound("questcomplete");
            Game1.addHUDMessage(new HUDMessage("✅ Назначение выполнено! Поговорите с Харви.", HUDMessage.achievement_type));
            _treatmentService.MarkTreatmentReadyForReviewByEpisode(episodeId);
        }

        private bool IsEpisodeObjectivesMet(string episodeId, TreatmentProgress progress)
        {
            var episode = _data.ActiveTreatmentEpisode;
            if (episode == null)
                return false;

            return episodeId switch
            {
                StressEpisodes.PhysicalExhaustion =>
                    episode.RelatedCauseIds.Count > 0
                    && episode.RelatedCauseIds.All(progress.EpisodeCausesCompleted.Contains),

                StressEpisodes.Burnout =>
                    progress.BurnoutAvoidedMinesToday,

                StressEpisodes.AnxietySpike =>
                    progress.AnxietySafeSeconds >= EpisodeQuestRules.AnxietySafeSecondsRequired,

                _ => false,
            };
        }

        public void UpdateQuestJournal(string episodeId, TreatmentProgress progress)
        {
            if (!TreatmentEpisodeDefinitions.TryGet(episodeId, out var definition))
                return;

            _questService.UpdateQuest(
                definition.QuestId,
                objective: BuildObjectiveText(episodeId, progress, _data.ActiveTreatmentEpisode, _data.OverworkBreaksToday));
        }

        public static string BuildObjectiveText(
            string episodeId,
            TreatmentProgress progress,
            TreatmentEpisodeState? episode,
            int overworkBreaksToday = 0)
        {
            return episodeId switch
            {
                StressEpisodes.PhysicalExhaustion =>
                    BuildPhysicalExhaustionObjective(progress, episode, overworkBreaksToday),

                StressEpisodes.Burnout =>
                    BuildBurnoutObjective(progress),

                StressEpisodes.AnxietySpike =>
                    BuildAnxietySpikeObjective(progress),

                StressEpisodes.GotoroFlashback =>
                    BuildGotoroObjective(progress),

                _ => "Выполните назначение Харви и поговорите с ним.",
            };
        }

        /// <summary>Короткая строка прогресса для HUD и окна «План Харви» (те же данные, что у journal objective).</summary>
        public static string BuildCompactProgressLine(
            string episodeId,
            TreatmentProgress progress,
            int overworkBreaksToday = 0,
            TreatmentEpisodeState? episode = null)
        {
            return episodeId switch
            {
                StressEpisodes.PhysicalExhaustion =>
                    BuildPhysicalCompactProgress(progress, episode, overworkBreaksToday),

                StressEpisodes.Burnout =>
                    progress.BurnoutAvoidedMinesToday
                        ? "без шахт сегодня"
                        : "нужен день без шахт",

                StressEpisodes.AnxietySpike =>
                    $"{Math.Min(progress.AnxietySafeSeconds, EpisodeQuestRules.AnxietySafeSecondsRequired)}/{EpisodeQuestRules.AnxietySafeSecondsRequired} сек",

                StressEpisodes.GotoroFlashback =>
                    $"{progress.SecondsNearHarvey} сек укрытия",

                _ => "",
            };
        }

        private static string BuildPhysicalCompactProgress(
            TreatmentProgress progress,
            TreatmentEpisodeState? episode,
            int overworkBreaksToday)
        {
            int completed = progress.EpisodeCausesCompleted.Count;
            int total = episode?.RelatedCauseIds.Count ?? Math.Max(completed, 1);

            if (completed > 0)
                return $"{completed}/{total} симптомов";

            if (overworkBreaksToday > 0)
                return $"перерывы {overworkBreaksToday}/{EpisodeQuestRules.PhysicalOverworkBreaksRequired}";

            if (progress.WarmSeconds > 0)
                return $"тепло {Math.Min(progress.WarmSeconds, EpisodeQuestRules.PhysicalWarmSecondsRequired)}/{EpisodeQuestRules.PhysicalWarmSecondsRequired} сек";

            if (progress.TiredRestSeconds > 0)
                return $"отдых {Math.Min(progress.TiredRestSeconds, EpisodeQuestRules.PhysicalTiredRestSecondsRequired)}/{EpisodeQuestRules.PhysicalTiredRestSecondsRequired} сек";

            return "симптомы в работе";
        }

        private static string BuildPhysicalExhaustionObjective(
            TreatmentProgress progress,
            TreatmentEpisodeState? episode,
            int overworkBreaksToday)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Восстановитесь по каждому активному симптому:");
            sb.AppendLine();

            if (episode == null)
                return sb.ToString().TrimEnd();

            foreach (var causeId in episode.RelatedCauseIds)
            {
                var done = progress.EpisodeCausesCompleted.Contains(causeId);
                var line = causeId switch
                {
                    StressCauses.Hunger => done
                        ? "✅ Поесть"
                        : "⬜ Поесть любое блюдо",

                    StressCauses.TooCold => done
                        ? "✅ Согреться"
                        : $"⬜ Согреться ({Math.Min(progress.WarmSeconds, EpisodeQuestRules.PhysicalWarmSecondsRequired)}/{EpisodeQuestRules.PhysicalWarmSecondsRequired} сек в тёплой зоне)",

                    StressCauses.Tired => done
                        ? "✅ Отдых дома"
                        : $"⬜ Отдых дома без инструментов ({Math.Min(progress.TiredRestSeconds, EpisodeQuestRules.PhysicalTiredRestSecondsRequired)}/{EpisodeQuestRules.PhysicalTiredRestSecondsRequired} сек)",

                    StressCauses.Overwork => done
                        ? "✅ Перерыв от работы"
                        : $"⬜ Сделать перерыв ({overworkBreaksToday}/{EpisodeQuestRules.PhysicalOverworkBreaksRequired})",

                    StressCauses.NoSleep => done
                        ? "✅ Лечь до 22:00"
                        : "⬜ Лечь спать до 22:00 (проверка в конце дня)",

                    _ => done ? $"✅ {causeId}" : $"⬜ {causeId}",
                };

                sb.AppendLine(line);
            }

            sb.AppendLine();
            sb.AppendLine("Затем " + StressObjectiveTone.TalkToHarvey());
            return sb.ToString().TrimEnd();
        }

        private static string BuildBurnoutObjective(TreatmentProgress progress)
        {
            var informal = HarveyFriendshipHelper.IsDatingHarvey() || HarveyFriendshipHelper.IsMarriedToHarvey();
            var minesLine = progress.BurnoutAvoidedMinesToday
                ? "✅ Без шахт сегодня"
                : informal
                    ? "⚠️ Ты была в шахте — нужен новый день без шахт"
                    : "⚠️ Вы были в шахте — нужен новый день без шахт";

            var talkHarvey = informal
                ? "Затем поговори с Харви."
                : "Затем поговорите с Харви.";

            return $"""
                Остановитесь, не «дотяните ещё чуть-чуть»:

                {minesLine}
                ⬜ Лечь спать до 22:00 (проверка при отходе ко сну)

                {talkHarvey}
                """.Trim();
        }

        private static string BuildAnxietySpikeObjective(TreatmentProgress progress)
        {
            var required = EpisodeQuestRules.AnxietySafeSecondsRequired;
            var seconds = Math.Min(progress.AnxietySafeSeconds, required);

            if (progress.AnxietySafeSeconds >= required || progress.AnxietySpikeCompletionAnnounced)
            {
                return """
                    Вы справились с пиком тревоги.
                    Теперь поговорите с Харви.
                    """.Trim();
            }

            if (GameStateHelper.IsAnxietySafeLocation())
            {
                return $"""
                    Останьтесь в безопасном месте.
                    Прогресс: {seconds}/{required} сек.
                    """.Trim();
            }

            return $"""
                Найдите тихое безопасное место.
                Подойдут: дом, клиника, лес или спокойный угол.
                Прогресс: {seconds}/{required} сек.
                """.Trim();
        }

        private static string BuildGotoroObjective(TreatmentProgress progress)
        {
            return $"""
                Вернитесь в настоящее:

                Укрытие в лесу: {progress.SecondsNearHarvey} сек (стабилизация во flashback)
                Поговорите с Харви после стабилизации.
                """.Trim();
        }
    }
}
