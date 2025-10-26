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

        /// <summary>
        /// Обновляет прогресс лечения (вызывается каждую секунду)
        /// Теперь использует служебные флаги вместо проверки квестов в журнале
        /// </summary>
        public void UpdateTreatmentProgress(bool harveyNearby)
        {
            // Используем служебные флаги для определения активных лечений
            var activeTreatments = _data.StressState.TreatmentFlags.GetActiveTreatments().ToList();
            
            if (activeTreatments.Count == 0)
            {
                // Нет активных лечений
                return;
            }

            foreach (var buffId in activeTreatments)
            {
                // Получаем лечение из наших данных
                var treatment = _data.StressState.GetActiveTreatment(buffId);
                if (treatment == null)
                {
                    continue;
                }

                var questId = treatment.QuestId;
                var progress = treatment.Progress;

                if (progress == null)
                {
                    continue;
                }

                // Проверяем, нужно ли обрабатывать этот бафф (оптимизация)
                if (!ShouldProcessBuff(buffId, progress, harveyNearby))
                {
                    continue; // Пропускаем баффы, которые не требуют обработки
                }

                // Проверяем, нужно ли обновлять прогресс (с интервалом для оптимизации)
                if (!_data.StressState.TreatmentFlags.ShouldUpdateProgress(buffId, skipInterval: 3))
                {
                    continue; // Пропускаем обновление для оптимизации
                }

                UpdateSpecificTreatment(questId, buffId, progress, harveyNearby);
            }
        }

        /// <summary>
        /// Проверяет, нужно ли обрабатывать данный бафф
        /// </summary>
        private bool ShouldProcessBuff(string buffId, TreatmentProgress progress, bool harveyNearby)
        {
            return buffId switch
            {
                // Только при активных условиях лечения
                BuffIds.Thunder => harveyNearby,
                BuffIds.Darkness => Game1.timeOfDay >= 2000 && !(Game1.currentLocation?.IsOutdoors == true),
                BuffIds.TooCold => GameStateHelper.IsInWarmZone(),
                BuffIds.Social => harveyNearby || progress.SocialTalksAfterQuest > 0,
                
                // Проверки, которые уже выполнены
                BuffIds.Hunger => progress.AteAnyFood,
                BuffIds.NoSleep => progress.EarlySleepStreak >= 3,
                
                // Не требуют обработки (прогресс обновляется в других местах)
                BuffIds.Lonely => false,
                BuffIds.Overwork => false,
                
                _ => true // По умолчанию обрабатываем
            };
        }

        /// <summary>
        /// Получает ID квеста для данного баффа
        /// </summary>
        private string GetQuestIdForBuff(string buffId)
        {
            // Маппинг баффов на квесты (можно вынести в константы)
            return buffId switch
            {
                BuffIds.Tired => QuestIds.Tired,
                BuffIds.Lonely => QuestIds.Lonely,
                BuffIds.Thunder => QuestIds.Thunder,
                BuffIds.Hunger => QuestIds.Hunger,
                BuffIds.Overwork => QuestIds.Overwork,
                BuffIds.NoSleep => QuestIds.NoSleep,
                BuffIds.TooCold => QuestIds.TooCold,
                BuffIds.Social => QuestIds.Social,
                BuffIds.Darkness => QuestIds.Darkness,
                _ => buffId.Replace("Buff", "Quest") // Fallback для автоматического маппинга
            };
        }

        private void UpdateSpecificTreatment(string questId, string buffId, TreatmentProgress progress, bool harveyNearby)
        {
            switch (buffId)
            {
                case BuffIds.Thunder:
                    // Только если Харви рядом (активное лечение)
                    if (harveyNearby)
                    {
                        progress.SecondsNearHarvey++;
                        if (progress.SecondsNearHarvey >= 120)
                            _treatmentService.CompleteTreatment(buffId, "Мы пережили грозу вместе.");
                    }
                    break;

                case BuffIds.Darkness:
                    // Только ночью в помещении (активное лечение)
                    bool night = Game1.timeOfDay >= 2000;
                    bool indoors = !(Game1.currentLocation?.IsOutdoors == true && night);
                    if (night && indoors)
                    {
                        progress.EveningInLightSeconds++;
                        if (progress.EveningInLightSeconds >= 180)
                            _treatmentService.CompleteTreatment(buffId, "Свет сильнее тьмы.");
                    }
                    break;

                // Lonely: прогресс обновляется в ModEntry при разговоре с NPC
                // Нет необходимости проверять каждую секунду

                // Overwork: прогресс обновляется в CheckOverworkBreakCompleteTrigger
                // Нет необходимости проверять каждую секунду

                case BuffIds.Hunger:
                    // Проверка уже выполнена в DetectFoodConsumption
                    if (progress.AteAnyFood)
                        _treatmentService.CompleteTreatment(buffId, "Организм получил топливо.");
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

                case BuffIds.NoSleep:
                    // Проверка уже выполнена при отходе ко сну
                    if (progress.EarlySleepStreak >= 3)
                        _treatmentService.CompleteTreatment(buffId, "Сон восстановлен.");
                    break;

                case BuffIds.Social:
                    // Только если Харви рядом (активное лечение времени)
                    if (harveyNearby || progress.SocialTalksAfterQuest > 0)
                    {
                        // Отладка: логируем только при старте лечения (не каждую секунду)
                        if (progress.SecondsNearHarvey == 0 && progress.SocialTalksAfterQuest == 0)
                        {
                            _monitor.Log($"[SocialAnxiety] Начато лечение социальной тревожности", LogLevel.Debug);
                        }
                        
                        UpdateSocialAnxietyProgress(progress, harveyNearby);
                    }
                    break;
            }

            // Обновляем прогресс через StateService
            _stateService.UpdateProgress(questId, p => {
                p.SecondsNearHarvey = progress.SecondsNearHarvey;
                p.EveningInLightSeconds = progress.EveningInLightSeconds;
                p.TalkedUniqueToday = progress.TalkedUniqueToday;
                p.SocialTalksAfterQuest = progress.SocialTalksAfterQuest;
                p.AteAnyFood = progress.AteAnyFood;
                p.WarmSeconds = progress.WarmSeconds;
                p.EarlySleepStreak = progress.EarlySleepStreak;
            });
        }

        /// <summary>
        /// ⭐ ИСПРАВЛЕНО: Обновляет прогресс лечения социальной тревожности
        /// Обновляет прогресс лечения социальной тревожности
        /// </summary>
        private void UpdateSocialAnxietyProgress(TreatmentProgress progress, bool harveyNearby)
        {
            // Обновляем время с Харви (возвращает true если были изменения)
            bool timeChanged = UpdateHarveyTimeProgress(progress, harveyNearby);

            // Отладка: логируем только при изменении времени (не каждую секунду)
            if (timeChanged)
            {
                _monitor.Log($"[SocialAnxiety] Прогресс: время с Харви={progress.SecondsNearHarvey} сек, разговоров={progress.SocialTalksAfterQuest}", LogLevel.Debug);
                
                // Проверяем завершение квеста только если изменилось время
                // (разговоры проверяются в ModEntry при изменении)
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

            // Обновляем описание квеста только каждые 5 секунд для оптимизации
            if (progress.SecondsNearHarvey % 5 == 0)
            {
                UpdateQuestDescription(progress);
            }

            // HUD уведомления для времени с Харви (только при достижении ключевых моментов)
            switch (progress.SecondsNearHarvey)
            {
                case 15:
                    _monitor.Log($"[SocialAnxiety] Достигнуто 15 сек с Харви", LogLevel.Debug);
                    Game1.addHUDMessage(new HUDMessage("Время с Харви: 15/60 сек", HUDMessage.newQuest_type));
                    return true;
                case 30:
                    _monitor.Log($"[SocialAnxiety] Достигнуто 30 сек с Харви", LogLevel.Debug);
                    Game1.addHUDMessage(new HUDMessage("Время с Харви: 30/60 сек", HUDMessage.newQuest_type));
                    return true;
                case 45:
                    _monitor.Log($"[SocialAnxiety] Достигнуто 45 сек с Харви", LogLevel.Debug);
                    Game1.addHUDMessage(new HUDMessage("Время с Харви: 45/60 сек", HUDMessage.newQuest_type));
                    return true;
                case 60:
                    _monitor.Log($"[SocialAnxiety] Достигнуто 60 сек с Харви - лечение времени завершено", LogLevel.Debug);
                    Game1.addHUDMessage(new HUDMessage("✅ Время с Харви: 60/60 сек!", HUDMessage.achievement_type));
                    return true;
            }

            return false;
        }

        /// <summary>
        /// ⭐ НОВОЕ: Публичный метод для обновления описания квеста (вызывается из ModEntry)
        /// Обновляет описание квеста
        /// </summary>
        public void UpdateQuestDescription(TreatmentProgress progress)
        {
            string progressText = GetProgressText(progress);

            // Логирование убрано для оптимизации

            _questService.UpdateQuestDescription(QuestIds.Social,
                $"Харви предложил мягкую экспозицию: поговори с людьми и проведи время рядом с ним.\n\n{progressText}");
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
            int conversationsAfterQuest = progress.SocialTalksAfterQuest;
            int timeWithHarvey = progress.SecondsNearHarvey;

            // ⭐ ИЗМЕНЕНО: Путь 1: 3 разговора + 60 сек с Харви (оба условия)
            if (conversationsAfterQuest >= 3 && timeWithHarvey >= 60)
            {
                _monitor.Log($"[CheckQuestCompletion] ✅ Квест завершен (путь 1): {conversationsAfterQuest} разговоров + {timeWithHarvey} сек", LogLevel.Info);

                Game1.addHUDMessage(new HUDMessage("✅ Социальная тренировка завершена! (3 разговора + время с Харви)", HUDMessage.achievement_type));
                _treatmentService.CompleteTreatment(BuffIds.Social, "Социальный дискомфорт прошел! Ты отлично справилась с тренировкой.");
                return; // ⭐ ВАЖНО: выходим, чтобы не срабатывало второе условие
            }

            // ⭐ ИЗМЕНЕНО: Путь 2: 5 разговоров (альтернативное условие)
            if (conversationsAfterQuest >= 5)
            {
                _monitor.Log($"[CheckQuestCompletion] ✅ Квест завершен (путь 2): {conversationsAfterQuest} разговоров", LogLevel.Info);

                Game1.addHUDMessage(new HUDMessage("✅ Социальная тренировка завершена! (5 разговоров)", HUDMessage.achievement_type));
                _treatmentService.CompleteTreatment(BuffIds.Social, "Социальный дискомфорт прошел! Ты стала увереннее в общении.");
            }
            else
            {
                // Отладка: логируем текущий прогресс (без Info уровня)
                _monitor.Log($"[CheckQuestCompletion] Прогресс проверен: разговоров={conversationsAfterQuest}, время={timeWithHarvey} сек. Условия еще не выполнены", LogLevel.Debug);
            }
        }
    }
}

