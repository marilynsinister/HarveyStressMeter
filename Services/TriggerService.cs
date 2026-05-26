using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;
using StardewModdingAPI;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Models;
using HarveyStressMeter.Helpers;

namespace HarveyStressMeter.Services
{
    /// <summary>
    /// Сервис для управления триггерами (Manual triggers и прогрессом квестов)
    /// </summary>
    public class TriggerService
    {
        private const int TiredRestSecondsRequired = 60;
        private const int ThunderSecondsRequired = 120;
        private const int OverworkBreakSecondsRequired = 30;

        private readonly SaveData _data;
        private readonly BuffService _buffService;
        private readonly QuestService _questService;
        private readonly TreatmentService _treatmentService;
        private readonly StateService _stateService;
        private readonly IMonitor _monitor;

        public TriggerService(SaveData data, BuffService buffService, QuestService questService, StateService stateService, TreatmentService treatmentService, IMonitor monitor)
        {
            _data = data;
            _buffService = buffService;
            _questService = questService;
            _stateService = stateService;
            _treatmentService = treatmentService;
            _monitor = monitor;
        }

        /// <summary>
        /// Проверяет Manual триггеры (должны вызываться периодически)
        /// </summary>
        public void CheckManualTriggers()
        {
            CheckTiredRestTrigger();
            CheckThunderCalmingTrigger();
            CheckOverworkBreakCompleteTrigger();
            CheckSocialQuestCompleteTrigger();  // ⭐ ДОБАВЛЕНО: проверка завершения Social квеста
        }

        private void CheckTiredRestTrigger()
        {
            // Завершение Tired — через UpdateTiredRestProgress (tick, 60 сек дома без инструментов).
            // Instant-complete и gate !RestingAtHome удалены (C-02): конфликтовали с buffRestingAtHome.
        }

        private void CheckThunderCalmingTrigger()
        {
            // Завершение Thunder — через UpdateThunderProgress (120 сек, Hospital, дождь/гроза, Harvey рядом).
            // Legacy instant-complete при !CalmingAtHospital отключён (M-04).
        }

        private void CheckOverworkBreakCompleteTrigger()
        {
            // Завершение перерывов Overwork — через UpdateOverworkBreakProgress (tick, 30 сек в rest zone).
            // Legacy CP trigger path (topic + !buff) отключён (C-03): CP TriggerAction не wired.
        }

        private void CheckSocialQuestCompleteTrigger()
        {
            if (!_stateService.HasActiveQuestState(QuestIds.Social)) return;

            var socialTreatment = _data.StressState.GetActiveTreatmentByQuest(QuestIds.Social);
            if (socialTreatment?.Progress == null) return;

            if (socialTreatment.Progress.IsSocialQuestCompleted())
            {
                var completionPath = socialTreatment.Progress.GetSocialCompletionPath();

                _monitor.Log($"[Social Quest] Завершение квеста по пути: {completionPath}", LogLevel.Info);
                _monitor.Log($"[Social Quest] Разговоры после квеста: {socialTreatment.Progress.SocialTalksAfterQuest}", LogLevel.Info);
                _monitor.Log($"[Social Quest] Время с Харви: {socialTreatment.Progress.SecondsNearHarvey} сек", LogLevel.Info);

                Game1.playSound("questcomplete");
                _treatmentService.MarkTreatmentReadyForReview(BuffIds.Social);
            }
        }

        /// <summary>
        /// Обновляет прогресс лечения (вызывается каждую секунду)
        /// </summary>
        public void UpdateTreatmentProgress(bool harveyNearby)
        {
            foreach (var treatment in _data.StressState.ActiveTreatments.Values)
            {
                // ⭐ ИСПРАВЛЕНО: Пропускаем завершенные лечения
                if (treatment.IsCured || treatment.IsCompleted)
                    continue;

                if (treatment.Progress == null || string.IsNullOrEmpty(treatment.QuestId))
                    continue;

                UpdateSpecificTreatment(treatment.QuestId, treatment.BuffId, treatment.Progress, harveyNearby);
            }
        }


        private void UpdateSpecificTreatment(string questId, string buffId, TreatmentProgress progress, bool harveyNearby)
        {
            switch (buffId)
            {
                case BuffIds.Thunder:
                    UpdateThunderProgress(progress, harveyNearby);
                    break;

                case BuffIds.Darkness:
                    bool night = Game1.timeOfDay >= 2000;
                    bool indoors = !(Game1.currentLocation?.IsOutdoors == true);
                    if (night && indoors)
                    {
                        progress.EveningInLightSeconds++;
                        if (progress.EveningInLightSeconds >= 180)
                            _treatmentService.MarkTreatmentReadyForReview(buffId);
                    }
                    break;

                case BuffIds.TooCold:
                    // Только если в теплой зоне
                    if (GameStateHelper.IsInWarmZone())
                    {
                        progress.WarmSeconds++;
                        if (progress.WarmSeconds >= 120)
                            _treatmentService.MarkTreatmentReadyForReview(buffId);
                    }
                    break;

                case BuffIds.Tired:
                    UpdateTiredRestProgress(progress);
                    break;

                case BuffIds.Overwork:
                    UpdateOverworkBreakProgress();
                    break;

                case BuffIds.Social:
                    UpdateSocialAnxietyProgress(progress, harveyNearby);
                    break;
            }

            // Обновляем прогресс через StateService
            _stateService.UpdateProgress(questId, p =>
            {
                p.SecondsNearHarvey = progress.SecondsNearHarvey;
                p.EveningInLightSeconds = progress.EveningInLightSeconds;
                p.TalkedUniqueToday = progress.TalkedUniqueToday;
                p.SocialTalksAfterQuest = progress.SocialTalksAfterQuest;
                p.AteAnyFood = progress.AteAnyFood;
                p.WarmSeconds = progress.WarmSeconds;
                p.EarlySleepStreak = progress.EarlySleepStreak;
                p.TiredRestSeconds = progress.TiredRestSeconds;
            });
        }

        private void UpdateThunderProgress(TreatmentProgress progress, bool harveyNearby)
        {
            if (!_stateService.HasActiveQuestState(QuestIds.Thunder))
                return;

            if (Game1.CurrentEvent != null)
                return;

            if (!IsThunderQuestProgressConditionsMet(harveyNearby))
                return;

            progress.SecondsNearHarvey++;

            switch (progress.SecondsNearHarvey)
            {
                case 30:
                    Game1.addHUDMessage(new HUDMessage("Гроза — рядом с Харви: 30/120 сек", HUDMessage.newQuest_type));
                    UpdateThunderQuestDescription(progress);
                    break;
                case 60:
                    Game1.addHUDMessage(new HUDMessage("Гроза — рядом с Харви: 60/120 сек", HUDMessage.newQuest_type));
                    UpdateThunderQuestDescription(progress);
                    break;
                case 90:
                    Game1.addHUDMessage(new HUDMessage("Гроза — рядом с Харви: 90/120 сек", HUDMessage.newQuest_type));
                    UpdateThunderQuestDescription(progress);
                    break;
                case 120:
                    UpdateThunderQuestDescription(progress);
                    break;
            }

            if (progress.SecondsNearHarvey >= ThunderSecondsRequired)
                CompleteThunderTreatment();
        }

        private static bool IsThunderQuestProgressConditionsMet(bool harveyNearby)
        {
            return harveyNearby
                && Game1.player.currentLocation?.NameOrUniqueName == "Hospital"
                && (Game1.isLightning || Game1.isRaining);
        }

        private void CompleteThunderTreatment()
        {
            Game1.playSound("questcomplete");
            _treatmentService.MarkTreatmentReadyForReview(BuffIds.Thunder);
        }

        private bool IsAwaitingHarveyReview(string questId)
        {
            var treatment = _data.StressState.GetActiveTreatmentByQuest(questId);
            return treatment?.AwaitingHarveyReview == true;
        }

        private void UpdateThunderQuestDescription(TreatmentProgress progress)
        {
            if (IsAwaitingHarveyReview(QuestIds.Thunder))
                return;

            int seconds = Math.Min(progress.SecondsNearHarvey, ThunderSecondsRequired);
            string progressLine = progress.SecondsNearHarvey >= ThunderSecondsRequired
                ? "✅ Вы пережили грозу вместе!"
                : $"Рядом с Харви в клинике: {seconds}/{ThunderSecondsRequired} сек (нужен дождь или гроза)";

            _questService.UpdateQuest(QuestIds.Thunder,
                objective: $"Проведи время с Харви в клинике во время грозы.\n\n{progressLine}");
        }

        private void UpdateTiredRestProgress(TreatmentProgress progress)
        {
            if (!_stateService.HasActiveQuestState(QuestIds.Tired))
                return;

            if (Game1.CurrentEvent != null)
                return;

            bool restingAtHome = Game1.player.currentLocation is StardewValley.Locations.FarmHouse
                && !GameStateHelper.HasHeavyTools(Game1.player);

            if (!restingAtHome)
                return;

            progress.TiredRestSeconds++;

            switch (progress.TiredRestSeconds)
            {
                case 15:
                    Game1.addHUDMessage(new HUDMessage("Отдых дома: 15/60 сек", HUDMessage.newQuest_type));
                    UpdateTiredQuestDescription(progress);
                    break;
                case 30:
                    Game1.addHUDMessage(new HUDMessage("Отдых дома: 30/60 сек", HUDMessage.newQuest_type));
                    UpdateTiredQuestDescription(progress);
                    break;
                case 45:
                    Game1.addHUDMessage(new HUDMessage("Отдых дома: 45/60 сек", HUDMessage.newQuest_type));
                    UpdateTiredQuestDescription(progress);
                    break;
                case 60:
                    UpdateTiredQuestDescription(progress);
                    break;
            }

            if (progress.TiredRestSeconds >= TiredRestSecondsRequired)
            {
                Game1.playSound("questcomplete");
                _treatmentService.MarkTreatmentReadyForReview(BuffIds.Tired);
            }
        }

        private void UpdateTiredQuestDescription(TreatmentProgress progress)
        {
            int seconds = Math.Min(progress.TiredRestSeconds, TiredRestSecondsRequired);
            string progressLine = progress.TiredRestSeconds >= TiredRestSecondsRequired
                ? "✅ Отдых дома завершён!"
                : $"Отдых дома: {seconds}/{TiredRestSecondsRequired} сек (уберите кирку, топор и мотыгу)";

            _questService.UpdateQuest(QuestIds.Tired,
                objective: $"Отдохни в фермерском доме, избегая тяжёлой работы.\n\n{progressLine}");
        }

        private void UpdateOverworkBreakProgress()
        {
            if (!_stateService.HasActiveQuestState(QuestIds.Overwork))
                return;

            if (Game1.CurrentEvent != null)
                return;

            if (_data.OverworkBreaksToday >= 3)
                return;

            if (!GameStateHelper.IsInRestZone())
            {
                if (_data.OverworkBreakSeconds > 0 || _data.OverworkBreakActive)
                {
                    _data.OverworkBreakSeconds = 0;
                    _data.OverworkBreakActive = false;
                }
                return;
            }

            if (!_stateService.HasBuffInGame(BuffIds.OverworkBreak))
                return;

            _data.OverworkBreakActive = true;
            _data.OverworkBreakSeconds++;

            if (_data.OverworkBreakSeconds < OverworkBreakSecondsRequired)
                return;

            _data.OverworkBreaksToday = Math.Min(3, _data.OverworkBreaksToday + 1);
            _data.OverworkBreakSeconds = 0;
            _data.OverworkBreakActive = false;

            Game1.playSound("reward");
            Game1.addHUDMessage(new HUDMessage($"Перерыв {_data.OverworkBreaksToday}/3", HUDMessage.achievement_type));

            if (_data.OverworkBreaksToday >= 3)
                CompleteOverworkTreatment();
        }

        private void CompleteOverworkTreatment()
        {
            Game1.playSound("questcomplete");
            ConversationHelper.RemoveTopic(TopicIds.OverworkBreakActive);
            ConversationHelper.RemoveTopic(TopicIds.OverworkBreakInterrupted);
            _buffService.RemoveBuff(BuffIds.OverworkBreak);
            _treatmentService.MarkTreatmentReadyForReview(BuffIds.Overwork);
        }

        private void UpdateSocialAnxietyProgress(TreatmentProgress progress, bool harveyNearby)
        {
            // ⭐ ИСПРАВЛЕНО: Обновляем время с Харви ТОЛЬКО если квест Social активен
            if (_stateService.HasActiveQuestState(QuestIds.Social))
            {
                // Обновляем время с Харви (возвращает true если были изменения)
                bool timeChanged = UpdateHarveyTimeProgress(progress, harveyNearby);

                // Отладка: логируем только при изменении времени (не каждую секунду)
                if (timeChanged)
                {
                    //_monitor.Log($"[SocialAnxiety] Прогресс: время с Харви={progress.SecondsNearHarvey} сек, разговоров={progress.SocialTalksAfterQuest}", LogLevel.Debug);
                }

                // ⭐ ИСПРАВЛЕНО: Проверяем завершение квеста каждый раз (CheckQuestCompletion сам проверит условия)
                CheckQuestCompletion(progress);
            }
        }

        /// <summary>
        /// Обновляет прогресс времени с Харви
        /// </summary>
        private bool UpdateHarveyTimeProgress(TreatmentProgress progress, bool harveyNearby)
        {
            if (!harveyNearby) return false;

            progress.SecondsNearHarvey++;

            // HUD уведомления для времени с Харви (только при достижении ключевых моментов)
            switch (progress.SecondsNearHarvey)
            {
                case 15:
                    Game1.addHUDMessage(new HUDMessage("Время с Харви: 15/60 сек", HUDMessage.newQuest_type));
                    UpdateQuestDescription(progress);
                    return true;
                case 30:
                    Game1.addHUDMessage(new HUDMessage("Время с Харви: 30/60 сек", HUDMessage.newQuest_type));
                    UpdateQuestDescription(progress);
                    return true;
                case 45:
                    Game1.addHUDMessage(new HUDMessage("Время с Харви: 45/60 сек", HUDMessage.newQuest_type));
                    UpdateQuestDescription(progress);
                    return true;
                case 60:
                    Game1.addHUDMessage(new HUDMessage("✅ Время с Харви: 60/60 сек!", HUDMessage.achievement_type));
                    UpdateQuestDescription(progress);
                    return true;
            }

            return false;
        }

        public void UpdateQuestDescription(TreatmentProgress progress)
        {
            if (IsAwaitingHarveyReview(QuestIds.Social))
                return;

            string progressText = GetProgressText(progress);

            // Логирование убрано для оптимизации

            _questService.UpdateQuest(QuestIds.Social,
                description: $"Харви предложил мягкую экспозицию: поговори с людьми и проведи время рядом с ним.\n\n{progressText}");
        }


        /// <summary>
        /// ⭐ ИСПРАВЛЕНО: Возвращает текст прогресса с правильными условиями
        /// Возвращает текст прогресса для отображения
        /// </summary>
        private string GetProgressText(TreatmentProgress progress)
        {
            int conversationsAfterQuest = progress.SocialTalksAfterQuest;
            int timeWithHarvey = progress.SecondsNearHarvey;

            // ⭐ ИЗМЕНЕНО: Проверяем оба варианта завершения квеста

            // Вариант 1: 3 разговора + 60 сек с Харви (оба условия выполнены)
            if (conversationsAfterQuest >= 3 && timeWithHarvey >= 60)
            {
                return "✅ Задача выполнена! (3 разговора + время с Харви)";
            }

            // Вариант 2: 5 разговоров (альтернативное условие)
            if (conversationsAfterQuest >= 5)
            {
                return "✅ Задача выполнена! (5 разговоров)";
            }

            // ⭐ НОВОЕ: Показываем прогресс для обоих путей
            // Если выполнено условие с разговорами для варианта 1
            if (conversationsAfterQuest >= 3)
            {
                return $"Поговорили с персонажами: {conversationsAfterQuest}/3 ✅ | Время с Харви: {timeWithHarvey}/60 сек";
            }

            // ⭐ НОВОЕ: Обычный прогресс (ни одно условие не выполнено)
            return $"Поговорили с персонажами: {conversationsAfterQuest}/3 (или 5) | Время с Харви: {timeWithHarvey}/60 сек";
        }

        /// <summary>
        /// ⭐ ИСПРАВЛЕНО: Проверяет и завершает квест при выполнении условий
        /// Проверяет и завершает квест при выполнении условий
        /// </summary>
        public void CheckQuestCompletion(TreatmentProgress progress)
        {
            // ⭐ ИСПРАВЛЕНО: Проверяем, что квест еще активен перед завершением
            // Это предотвращает повторные вызовы после завершения
            if (!_stateService.HasActiveQuestState(QuestIds.Social))
            {
                return; // Квест уже завершен, ничего не делаем
            }

            if (progress.SocialTalksAfterQuest >= 3 && progress.SecondsNearHarvey >= 60)
            {
                _monitor.Log($"[Social Quest] ✅ Квест завершен: 3 разговора + 60 сек с Харви", LogLevel.Info);
                Game1.addHUDMessage(new HUDMessage("✅ Социальная тренировка завершена! (3 разговора + время с Харви)", HUDMessage.achievement_type));

                _treatmentService.MarkTreatmentReadyForReview(BuffIds.Social);

                _monitor.Log($"[Social Quest] Условия выполнены — ожидание разговора с Харви", LogLevel.Info);
                return;
            }

            if (progress.SocialTalksAfterQuest >= 5)
            {
                _monitor.Log($"[Social Quest] ✅ Квест завершен: 5 разговоров", LogLevel.Info);
                Game1.addHUDMessage(new HUDMessage("✅ Социальная тренировка завершена! (5 разговоров)", HUDMessage.achievement_type));

                _treatmentService.MarkTreatmentReadyForReview(BuffIds.Social);

                _monitor.Log($"[Social Quest] Условия выполнены — ожидание разговора с Харви", LogLevel.Info);
                return;
            }

            UpdateQuestDescription(progress);
        }
    }
}

