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
            if (!_stateService.HasQuestInJournal(QuestIds.Tired)) return;

            if (Game1.player.currentLocation is StardewValley.Locations.FarmHouse
                && !GameStateHelper.HasHeavyTools(Game1.player)
                && !_stateService.HasActiveBuffInGame(BuffIds.RestingAtHome))
            {
                Game1.playSound("questcomplete");
                _questService.CompleteQuest(QuestIds.Tired);
                _buffService.RemoveBuff(BuffIds.Tired);
                ConversationHelper.AddTopic("topicStressTreatmentTiredCured", 2);
                _stateService.CompleteTreatment(QuestIds.Tired);
            }
        }

        private void CheckThunderCalmingTrigger()
        {
            if (!_stateService.HasQuestInJournal(QuestIds.Thunder)) return;

            if (Game1.player.currentLocation?.NameOrUniqueName == "Hospital"
                && (Game1.isLightning || Game1.isRaining)
                && !_stateService.HasActiveBuffInGame(BuffIds.CalmingAtHospital))
            {
                Game1.playSound("questcomplete");
                _questService.CompleteQuest(QuestIds.Thunder);
                _buffService.RemoveBuff(BuffIds.Thunder);
                ConversationHelper.AddTopic("topicStressTreatmentThunderCured", 2);
                _stateService.CompleteTreatment(QuestIds.Thunder);
            }
        }

        private void CheckOverworkBreakCompleteTrigger()
        {
            if (!_stateService.HasQuestInJournal(QuestIds.Overwork)) return;
            if (_stateService.HasActiveBuffInGame(BuffIds.OverworkBreak)) return;

            if (ConversationHelper.HasTopic(TopicIds.OverworkBreakActive)
                && !ConversationHelper.HasTopic(TopicIds.OverworkBreakInterrupted))
            {
                _data.OverworkBreaksToday++;
                Game1.playSound("reward");
                Game1.addHUDMessage(new HUDMessage($"+1 отдых ({_data.OverworkBreaksToday}/3)", HUDMessage.achievement_type));
                ConversationHelper.RemoveTopic(TopicIds.OverworkBreakActive);
            }
        }

        private void CheckSocialQuestCompleteTrigger()
        {
            if (!_stateService.HasQuestInJournal(QuestIds.Social)) return;

            var socialTreatment = _data.StressState.GetActiveTreatmentByQuest(QuestIds.Social);
            if (socialTreatment?.Progress == null) return;

            if (socialTreatment.Progress.IsSocialQuestCompleted())
            {
                var completionPath = socialTreatment.Progress.GetSocialCompletionPath();

                _monitor.Log($"[Social Quest] Завершение квеста по пути: {completionPath}", LogLevel.Info);
                _monitor.Log($"[Social Quest] Разговоры после квеста: {socialTreatment.Progress.SocialTalksAfterQuest}", LogLevel.Info);
                _monitor.Log($"[Social Quest] Время с Харви: {socialTreatment.Progress.SecondsNearHarvey} сек", LogLevel.Info);

                Game1.playSound("questcomplete");
                _questService.CompleteQuest(QuestIds.Social);
                _buffService.RemoveBuff(BuffIds.Social);
                ConversationHelper.AddTopic("topicStressTreatmentSocialCured", 2);
                _stateService.CompleteTreatment(QuestIds.Social);
            }
        }

        /// <summary>
        /// Обновляет прогресс лечения (вызывается каждую секунду)
        /// </summary>
        public void UpdateTreatmentProgress(bool harveyNearby)
        {
            foreach (var treatment in _data.StressState.ActiveTreatments.Values)
            {
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
                    if (harveyNearby)
                    {
                        progress.SecondsNearHarvey++;
                        if (progress.SecondsNearHarvey >= 120)
                            _treatmentService.CompleteTreatment(buffId, "Мы пережили грозу вместе.");
                    }
                    break;

                case BuffIds.Darkness:
                    bool night = Game1.timeOfDay >= 2000;
                    bool indoors = !(Game1.currentLocation?.IsOutdoors == true);
                    if (night && indoors)
                    {
                        progress.EveningInLightSeconds++;
                        if (progress.EveningInLightSeconds >= 180)
                            _treatmentService.CompleteTreatment(buffId, "Свет сильнее тьмы.");
                    }
                    break;

                case BuffIds.TooCold:
                    // Только если в теплой зоне
                    if (GameStateHelper.IsInWarmZone())
                    {
                        progress.WarmSeconds++;
                        if (progress.WarmSeconds >= 120)
                            _treatmentService.CompleteTreatment(buffId, "Тепло вернуло силы.");
                    }
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
            });
        }

        private void UpdateSocialAnxietyProgress(TreatmentProgress progress, bool harveyNearby)
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
                    _monitor.Log($"[SocialAnxiety] Достигнуто 15 сек с Харви", LogLevel.Debug);
                    Game1.addHUDMessage(new HUDMessage("Время с Харви: 15/60 сек", HUDMessage.newQuest_type));
                    UpdateQuestDescription(progress);
                    return true;
                case 30:
                    _monitor.Log($"[SocialAnxiety] Достигнуто 30 сек с Харви", LogLevel.Debug);
                    Game1.addHUDMessage(new HUDMessage("Время с Харви: 30/60 сек", HUDMessage.newQuest_type));
                    UpdateQuestDescription(progress);
                    return true;
                case 45:
                    _monitor.Log($"[SocialAnxiety] Достигнуто 45 сек с Харви", LogLevel.Debug);
                    Game1.addHUDMessage(new HUDMessage("Время с Харви: 45/60 сек", HUDMessage.newQuest_type));
                    UpdateQuestDescription(progress);
                    return true;
                case 60:
                    _monitor.Log($"[SocialAnxiety] Достигнуто 60 сек с Харви - лечение времени завершено", LogLevel.Debug);
                    Game1.addHUDMessage(new HUDMessage("✅ Время с Харви: 60/60 сек!", HUDMessage.achievement_type));
                    UpdateQuestDescription(progress);
                    return true;
            }

            return false;
        }

        public void UpdateQuestDescription(TreatmentProgress progress)
        {
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
            if (progress.SocialTalksAfterQuest >= 3 && progress.SecondsNearHarvey >= 60)
            {
                Game1.addHUDMessage(new HUDMessage("✅ Социальная тренировка завершена! (3 разговора + время с Харви)", HUDMessage.achievement_type));
                _treatmentService.CompleteTreatment(BuffIds.Social, "Социальный дискомфорт прошел! Ты отлично справилась с тренировкой.");
                return;
            }

            if (progress.SocialTalksAfterQuest >= 5)
            {
                Game1.addHUDMessage(new HUDMessage("✅ Социальная тренировка завершена! (5 разговоров)", HUDMessage.achievement_type));
                _treatmentService.CompleteTreatment(BuffIds.Social, "Социальный дискомфорт прошел! Ты стала увереннее в общении.");
                return;
            }

            UpdateQuestDescription(progress);
        }
    }
}

