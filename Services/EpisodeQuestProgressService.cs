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

        public void InitializeEpisodeQuest(string episodeId, TreatmentProgress progress)
        {
            progress.EpisodeCausesCompleted.Clear();
            progress.AnxietySafeSeconds = 0;
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
        }

        public void UpdateActiveEpisode(bool harveyNearby)
        {
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
                case StressEpisodes.AnxietySpike:
                    TickAnxietySpike(progress);
                    break;
                case StressEpisodes.GotoroFlashback:
                    TickGotoroFlashback(progress);
                    break;
            }
        }

        public void OnPlayerWarped(string? locationName)
        {
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

            if (episodeId == StressEpisodes.AnxietySpike)
            {
                treatment.Progress.AnxietySafeSeconds = Math.Max(
                    treatment.Progress.AnxietySafeSeconds,
                    shelterSeconds);
                UpdateQuestJournal(StressEpisodes.AnxietySpike, treatment.Progress);

                if (treatment.Progress.AnxietySafeSeconds >= EpisodeQuestRules.AnxietySafeSecondsRequired)
                    TryCompleteEpisode(StressEpisodes.AnxietySpike, treatment.Progress);
            }
            else if (episodeId == StressEpisodes.GotoroFlashback)
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

        private void TickAnxietySpike(TreatmentProgress progress)
        {
            if (Game1.CurrentEvent != null || !GameStateHelper.IsAnxietySafeLocation())
                return;

            progress.AnxietySafeSeconds++;

            if (progress.AnxietySafeSeconds is 30 or 60)
            {
                Game1.addHUDMessage(new HUDMessage(
                    $"Безопасное место: {progress.AnxietySafeSeconds}/{EpisodeQuestRules.AnxietySafeSecondsRequired} сек",
                    HUDMessage.newQuest_type));
            }
            else if (progress.AnxietySafeSeconds == EpisodeQuestRules.AnxietySafeSecondsRequired)
            {
                Game1.addHUDMessage(new HUDMessage(
                    HarveyFriendshipHelper.IsDatingHarvey() || HarveyFriendshipHelper.IsMarriedToHarvey()
                        ? "✅ Ты пережила пик тревоги в безопасном месте"
                        : "✅ Вы пережили пик тревоги в безопасном месте",
                    HUDMessage.achievement_type));
            }

            UpdateQuestJournal(StressEpisodes.AnxietySpike, progress);

            if (progress.AnxietySafeSeconds >= EpisodeQuestRules.AnxietySafeSecondsRequired)
                TryCompleteEpisode(StressEpisodes.AnxietySpike, progress);
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
            var seconds = Math.Min(progress.AnxietySafeSeconds, EpisodeQuestRules.AnxietySafeSecondsRequired);
            var line = progress.AnxietySafeSeconds >= EpisodeQuestRules.AnxietySafeSecondsRequired
                ? "✅ Безопасное место: задача выполнена"
                : $"Безопасное место: {seconds}/{EpisodeQuestRules.AnxietySafeSecondsRequired} сек (дом, клиника, лес…)";

            return $"""
                Переждите пик тревоги в тихом месте:

                {line}

                Затем поговорите с Харви.
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
