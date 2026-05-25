using System;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using HarveyStressMeter.Services;
using HarveyStressMeter.Models;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using StardewModdingAPI.Utilities;

namespace HarveyStressMeter.Handlers
{
    /// <summary>
    /// Handles game logic: stress triggers, dialogues, quests
    /// Follows Single Responsibility Principle - only game mechanics
    /// </summary>
    public class GameLogicHandler
    {
        private readonly SaveData _data;
        private readonly IMonitor _monitor;
        private readonly TreatmentService _treatmentService;
        private readonly TriggerService _triggerService;
        private readonly BuffService _buffService;
        private readonly StateService _stateService;
        private readonly DarknessService _darknessService;
        private readonly StressDialogueService _stressDialogueService;

        private string? _lastDialogueNpc;
        private bool _suppressNextHarveyDialogueEnd;

        // ⭐ ОПТИМИЗАЦИЯ: Кэширование и интервальные проверки
        private bool _lastHarveyNearby = false;
        private int _harveyCheckCounter = 0;
        private const int HARVEY_CHECK_INTERVAL = 1; // Каждые 1 секунды

        private int _progressUpdateCounter = 0;
        private const int PROGRESS_UPDATE_INTERVAL = 1; // Каждые 1 секунд

        private int _tiredCheckCounter = 0;
        private const int TIRED_CHECK_INTERVAL = 10; // Каждые 10 секунд

        private bool _hasAnyStressBuffCached = false;
        private int _stressBuffCacheCounter = 0;
        private const int STRESS_BUFF_CACHE_INTERVAL = 5; // Каждые 5 секунд

        // Static mapping to avoid recreating dictionary on each call (DRY principle)
        private static readonly Dictionary<string, (string buffId, string questId, string displayName, bool isTreatmentTopic)> TopicMapping = new()
        {
            [TopicIds.StressTired] = (BuffIds.Tired, QuestIds.Tired, "Усталость", false),
            [TopicIds.StressLonely] = (BuffIds.Lonely, QuestIds.Lonely, "Одиночество", false),
            [TopicIds.StressThunder] = (BuffIds.Thunder, QuestIds.Thunder, "Страх грозы", false),
            [TopicIds.StressHunger] = (BuffIds.Hunger, QuestIds.Hunger, "Голод", false),
            [TopicIds.StressOverwork] = (BuffIds.Overwork, QuestIds.Overwork, "Переработка", false),
            [TopicIds.StressNoSleep] = (BuffIds.NoSleep, QuestIds.NoSleep, "Недосып", false),
            [TopicIds.StressTooCold] = (BuffIds.TooCold, QuestIds.TooCold, "Переохлаждение", false),
            [TopicIds.StressSocial] = (BuffIds.Social, QuestIds.Social, "Социальный дискомфорт", false),
            [TopicIds.StressDarkness] = (BuffIds.Darkness, QuestIds.Darkness, "Темнота", false),

            [TopicIds.TreatmentStartTired] = (BuffIds.Tired, QuestIds.Tired, "Усталость", true),
            [TopicIds.TreatmentStartLonely] = (BuffIds.Lonely, QuestIds.Lonely, "Одиночество", true),
            [TopicIds.TreatmentStartThunder] = (BuffIds.Thunder, QuestIds.Thunder, "Страх грозы", true),
            [TopicIds.TreatmentStartHunger] = (BuffIds.Hunger, QuestIds.Hunger, "Голод", true),
            [TopicIds.TreatmentStartOverwork] = (BuffIds.Overwork, QuestIds.Overwork, "Переработка", true),
            [TopicIds.TreatmentStartNoSleep] = (BuffIds.NoSleep, QuestIds.NoSleep, "Недосып", true),
            [TopicIds.TreatmentStartTooCold] = (BuffIds.TooCold, QuestIds.TooCold, "Переохлаждение", true),
            [TopicIds.TreatmentStartSocial] = (BuffIds.Social, QuestIds.Social, "Социальный дискомфорт", true),
            [TopicIds.TreatmentStartDarkness] = (BuffIds.Darkness, QuestIds.Darkness, "Темнота", true),
        };

        public GameLogicHandler(
            SaveData data,
            IMonitor monitor,
            TreatmentService treatmentService,
            TriggerService triggerService,
            BuffService buffService,
            StateService stateService,
            DarknessService darknessService,
            StressDialogueService stressDialogueService)
        {
            _data = data;
            _monitor = monitor;
            _treatmentService = treatmentService;
            _triggerService = triggerService;
            _buffService = buffService;
            _stateService = stateService;
            _darknessService = darknessService;
            _stressDialogueService = stressDialogueService;
        }

        /// <summary>Сбрасывает RAM-состояние programmatic stress-диалога (pending/consent).</summary>
        public void ClearStressDialoguePending() => _stressDialogueService.ClearPendingTreatment();

        public void ResetDailyData()
        {
            // ⭐ НОВОЕ: Проверяем топики вчерашнего дня ПЕРЕД очисткой
            // Это нужно для подсчета дней без разговоров/еды
            UpdateConsecutiveDaysCounters();

            _data.TalkedNpcsToday.Clear();
            _data.OverworkBreaksToday = 0;
            _data.OverworkBreakSeconds = 0;
            _data.OverworkBreakActive = false;
            _data.TalkedToHarveyToday = false;

            ResetDailyQuestCounters();
            
            // Обновляем состояние страха темноты каждый день
            _darknessService.UpdateDailyFearState();
        }

        public void CheckDayStartedStressTriggers()
        {
            // ⭐ ИСПРАВЛЕНО: Tired проверка убрана из начала дня
            // В начале дня stamina всегда полная, поэтому проверка Stamina <= 10 никогда не сработает
            // Проверка перенесена в CheckTiredStressTrigger() - вызывается в течение дня

            // Thunder - lightning
            if (Game1.stats.DaysPlayed >= 2
                && Game1.isLightning
                && !_stateService.HasActiveTreatmentState(BuffIds.Thunder)
                && !_stateService.HasImmunity(BuffIds.Thunder))
            {
                _treatmentService.ApplyStressBuff(BuffIds.Thunder, "Страх грозы");
            }

            // TooCold — проверяется при смене времени (вечер) и warp (см. TryApplyTooColdStressTrigger)

            // ⭐ НОВОЕ: Lonely - несколько дней без разговоров
            if (Game1.stats.DaysPlayed >= 3
                && _data.DaysWithoutTalking >= 3
                && !_stateService.HasActiveTreatmentState(BuffIds.Lonely)
                && !_stateService.HasImmunity(BuffIds.Lonely))
            {
                _treatmentService.ApplyStressBuff(BuffIds.Lonely, "Одиночество");
                _monitor.Log($"[Lonely Stress] Триггер активирован: {_data.DaysWithoutTalking} дней без разговоров", LogLevel.Info);
            }

            // ⭐ НОВОЕ: Hunger - несколько дней без еды
            if (Game1.stats.DaysPlayed >= 3
                && _data.DaysWithoutEating >= 2
                && !_stateService.HasActiveTreatmentState(BuffIds.Hunger)
                && !_stateService.HasImmunity(BuffIds.Hunger))
            {
                _treatmentService.ApplyStressBuff(BuffIds.Hunger, "Слабость от голода");
                _monitor.Log($"[Hunger Stress] Триггер активирован: {_data.DaysWithoutEating} дней без еды", LogLevel.Info);
            }

            // ⭐ НОВОЕ: NoSleep - несколько дней позднего сна
            if (Game1.stats.DaysPlayed >= 3
                && _data.DaysWithLateSleep >= 3
                && !_stateService.HasActiveTreatmentState(BuffIds.NoSleep)
                && !_stateService.HasImmunity(BuffIds.NoSleep))
            {
                _treatmentService.ApplyStressBuff(BuffIds.NoSleep, "Недосып");
                _monitor.Log($"[NoSleep Stress] Триггер активирован: {_data.DaysWithLateSleep} дней позднего сна", LogLevel.Info);
            }
        }

        public void ProcessGameTick(bool harveyNearby)
        {
            if (!Context.IsWorldReady)
                return;

            // ⭐ ОПТИМИЗАЦИЯ: Интервальные счетчики
            _harveyCheckCounter++;
            _progressUpdateCounter++;
            _tiredCheckCounter++;
            _stressBuffCacheCounter++;

            // === КАЖДЫЕ 3 СЕКУНДЫ: Harvey proximity (66% ⬇️) ===
            if (_harveyCheckCounter >= HARVEY_CHECK_INTERVAL)
            {
                _harveyCheckCounter = 0;
                UpdateHarveyCareAura(harveyNearby);
                _lastHarveyNearby = harveyNearby;
            }
            else
            {
                // Используем кэшированное значение для других проверок
                harveyNearby = _lastHarveyNearby;
            }

            // === КАЖДУЮ СЕКУНДУ: прогресс лечения + manual triggers ===
            if (_progressUpdateCounter >= PROGRESS_UPDATE_INTERVAL)
            {
                _progressUpdateCounter = 0;

                // Обновляем прогресс терапии темноты
                _darknessService.UpdateTherapyProgress();

                // Обновляем прогресс лечений (самый важный процесс)
                if (_data.StressState.ActiveTreatments.Count > 0)
                {
                    _triggerService.UpdateTreatmentProgress(harveyNearby);
                    _treatmentService.EnsureLockedBuffsPersist();
                }

                // Manual triggers (Tired/Thunder/Overwork/Social complete paths) — раз в секунду, как в backup
                if (ShouldRunManualTriggers())
                    _triggerService.CheckManualTriggers();

                // Thunder calming buff (только если квест активен)
                if (_stateService.HasActiveQuestState(QuestIds.Thunder))
                {
                    ApplyThunderCalmingBuff(harveyNearby);
                }

                // Natural buff removal (только если есть активные баффы)
                if (_data.StressState.ActiveTreatments.Count > 0 || GetHasAnyStressBuff())
                {
                    NaturalBuffRemoval(harveyNearby);
                }
            }

            // === КАЖДЫЕ 10 СЕКУНД: Медленные проверки (90% ⬇️) ===
            if (_tiredCheckCounter >= TIRED_CHECK_INTERVAL)
            {
                _tiredCheckCounter = 0;

                // Проверяем усталость в течение дня
                if (!_stateService.HasActiveTreatmentState(BuffIds.Tired))
                {
                    CheckTiredStressTrigger();
                }
            }
        }

        /// <summary>
        /// Manual triggers не должны срабатывать во время event script или с открытым меню.
        /// </summary>
        private static bool ShouldRunManualTriggers()
        {
            if (Game1.CurrentEvent != null)
                return false;
            if (Game1.activeClickableMenu != null)
                return false;
            return true;
        }

        /// <summary>
        /// ⭐ ОПТИМИЗАЦИЯ: Отдельный метод для обновления Harvey care aura
        /// </summary>
        private void UpdateHarveyCareAura(bool harveyNearby)
        {
            if (harveyNearby)
            {
                if (!_buffService.HasBuff(BuffIds.CareAura))
                {
                    _buffService.ApplyBuff(BuffIds.CareAura, "Рядом с Харви",
                        new StardewValley.Buffs.BuffEffects { Defense = { +1 }, MaxStamina = { +10 } }, 2000);
                }
            }
            else if (_buffService.HasBuff(BuffIds.CareAura))
            {
                _buffService.RemoveBuff(BuffIds.CareAura);
            }
        }

        /// <summary>
        /// ⭐ ОПТИМИЗАЦИЯ: Кэшированная проверка наличия стрессовых баффов
        /// Обновляется каждые 5 секунд вместо каждого вызова
        /// </summary>
        private bool GetHasAnyStressBuff()
        {
            // Обновляем кэш каждые N секунд
            if (_stressBuffCacheCounter >= STRESS_BUFF_CACHE_INTERVAL)
            {
                _stressBuffCacheCounter = 0;
                _hasAnyStressBuffCached = HasAnyStressBuff();
            }

            return _hasAnyStressBuffCached;
        }

        /// <summary>
        /// Сбрасывает счетчики оптимизации (вызывается при смене дня)
        /// </summary>
        public void ResetOptimizationCounters()
        {
            _harveyCheckCounter = 0;
            _progressUpdateCounter = 0;
            _tiredCheckCounter = 0;
            _stressBuffCacheCounter = 0;
            _hasAnyStressBuffCached = false;
            _lastHarveyNearby = false;
        }

        /// <summary>
        /// Быстрая проверка наличия любого стрессового баффа
        /// </summary>
        private bool HasAnyStressBuff()
        {
            return _stateService.HasBuffInGame(BuffIds.Tired)
                || _stateService.HasBuffInGame(BuffIds.Lonely)
                || _stateService.HasBuffInGame(BuffIds.Thunder)
                || _stateService.HasBuffInGame(BuffIds.Hunger)
                || _stateService.HasBuffInGame(BuffIds.TooCold)
                || _stateService.HasBuffInGame(BuffIds.Darkness)
                || _stateService.HasBuffInGame(BuffIds.Social);
        }

        public void HandleMenuChanged(MenuChangedEventArgs e)
        {
            HandleDialogueEvents(e);
        }

        public void HandleWarped(WarpedEventArgs e)
        {
            if (e.NewLocation == null) return;

            // Используем новую систему уровней страха темноты
            _darknessService.CheckAndApplyDarknessBuff(e.NewLocation);
            
            // Обрабатываем посещение локации для терапии (Шаг 2)
            _darknessService.HandleLocationVisit(e.NewLocation.NameOrUniqueName);
            
            ApplyQuestLocationBuffs(e.NewLocation);
            TryApplyTooColdStressTrigger();
        }

        public void HandleTimeChanged(TimeChangedEventArgs e)
        {
            // Obmorok from tiredness (at 2:00 am = 2600 in 26-hour time)
            if (e.NewTime == 2600 && Game1.player.Stamina >= 0 && Game1.player.Stamina <= 5)
            {
                if (Game1.stats.DaysPlayed >= 1
                    && !_stateService.HasActiveTreatmentState(BuffIds.Overwork)
                    && !_stateService.HasImmunity(BuffIds.Overwork))
                {
                    _treatmentService.ApplyStressBuff(BuffIds.Overwork, "Переработка");
                }
            }

            if (e.NewTime >= 2100 && e.OldTime < 2100)
                TryApplyTooColdStressTrigger();

            // Lightning check every 10 minutes during storm
            if (Game1.isLightning && e.NewTime % 100 == 0)
            {
                CheckLightningStressTrigger();
            }
        }

        public void CheckDayEndingQuestCompletion()
        {
            // NoSleep - completion at early bedtime
            if (_stateService.HasActiveQuestState(QuestIds.NoSleep)
                && Game1.timeOfDay >= 600 && Game1.timeOfDay <= 2200)
            {
                _stateService.CompleteTreatment(QuestIds.NoSleep);
                ConversationHelper.AddTopic("topicStressTreatmentNoSleepCured", 2);
                Game1.playSound("questcomplete");
            }

            // Darkness - completion when spending evening in light
            if (_stateService.HasActiveQuestState(QuestIds.Darkness)
                && GameStateHelper.IsEveningNight(2000, 2600)
                && _buffService.HasBuff(BuffIds.LightAndSafe)
                && Game1.player.currentLocation is StardewValley.Locations.FarmHouse)
            {
                _buffService.RemoveBuff(BuffIds.LightAndSafe);
                ConversationHelper.AddTopic("topicStressTreatmentDarknessCured", 2);
                Game1.playSound("questcomplete");
                _stateService.CompleteTreatment(QuestIds.Darkness);
            }
        }

        /// <summary>
        /// ⭐ НОВОЕ: Отслеживает паттерн позднего отхода ко сну
        /// Вызывается в конце дня перед сохранением
        /// </summary>
        public void CheckLateSleepPattern()
        {
            int currentTime = Game1.timeOfDay;
            bool wentToSleepLate = false;

            // Поздний сон: после полуночи до 2:00 (2400–2600 в 26-hour time)
            if (GameStateHelper.IsAfterMidnightUntilTwoAm())
            {
                wentToSleepLate = true;
                _monitor.Log($"[LateSleep] Игрок лег спать поздно: {currentTime}", LogLevel.Info);
            }
            // ⭐ ИСПРАВЛЕНО: Проверяем критически низкую выносливость (0-5) как признак обморока от усталости
            else if (Game1.player.Stamina >= 0 && Game1.player.Stamina <= 5)
            {
                wentToSleepLate = true;
                _monitor.Log($"[LateSleep] Игрок упал от усталости (stamina={Game1.player.Stamina})", LogLevel.Info);
            }
            
            if (wentToSleepLate)
            {
                _data.DaysWithLateSleep++;
                _monitor.Log($"[LateSleep] Счетчик: {_data.DaysWithLateSleep} дней позднего сна подряд", LogLevel.Info);
            }
            else
            {
                // Лег спать вовремя - сбрасываем счетчик
                if (_data.DaysWithLateSleep > 0)
                {
                    _monitor.Log($"[LateSleep] Игрок лег спать вовремя ({currentTime}) - счетчик сброшен", LogLevel.Info);
                }
                _data.DaysWithLateSleep = 0;
            }
        }

        private void HandleDialogueEvents(MenuChangedEventArgs e)
        {
            if (e.NewMenu is DialogueBox && Game1.currentSpeaker is NPC npc && npc.Name != "Harvey")
            {
                _lastDialogueNpc = npc.Name;
                CheckSocialStressTrigger(npc);
            }
            else if (e.NewMenu is DialogueBox && Game1.currentSpeaker is NPC harveyNpc && harveyNpc.Name == "Harvey")
            {
                _lastDialogueNpc = "Harvey";
                HandleHarveyDialogue(harveyNpc, e);
            }
            else if (e.OldMenu is DialogueBox && _lastDialogueNpc != null && _lastDialogueNpc != "Harvey")
            {
                HandleDialogueEnd();
            }
            else if (e.OldMenu is DialogueBox && _lastDialogueNpc == "Harvey")
            {
                // Закрытие CP-диалога при перехвате — не считаем завершением разговора
                if (_suppressNextHarveyDialogueEnd)
                {
                    _suppressNextHarveyDialogueEnd = false;
                    return;
                }

                HandleHarveyDialogueEnd();
                _lastDialogueNpc = null;
            }
        }

        private void HandleHarveyDialogue(NPC harveyNpc, MenuChangedEventArgs e)
        {
            _monitor.Log($"[Диалог] Начался разговор с Харви. Текущие топики: {string.Join(", ", Game1.player.activeDialogueEvents.Keys.Where(k => k.Contains("Stress")))}", LogLevel.Info);
            _monitor.Log($"[Диалог] Дебафф Social активен: {_stateService.HasActiveTreatmentState(BuffIds.Social)}", LogLevel.Info);

            // ⭐ НОВОЕ: Проверяем наличие активного дебаффа без лечения
            // Если есть - показываем программный диалог вместо стандартного
            if (_stressDialogueService.ShouldShowStressDialogue(out var buffId, out var dialogueText))
            {
                _monitor.Log($"[Диалог] Обнаружен активный дебафф {buffId} без лечения. Показываем программный диалог.", LogLevel.Info);

                // Закрытие CP-диалога вызовет MenuChanged — подавляем ложное завершение разговора
                _suppressNextHarveyDialogueEnd = true;

                if (Game1.activeClickableMenu is DialogueBox)
                    Game1.activeClickableMenu = null;

                _stressDialogueService.ShowStressDialogue(buffId!, dialogueText!);
                _lastDialogueNpc = "Harvey";
                return;
            }

            // ⭐ FALLBACK: Если программный диалог не показан, используем старую систему топиков
            CheckAllStressDebuffsAndAddTopics();

            // ⭐ НОВОЕ: Проверяем дебаффы темноты и добавляем топики если их нет
            CheckDarknessDebuffsAndAddTopics();

            // ⭐ НОВОЕ: Проверяем топик начала терапии темноты
            CheckDarknessTherapyStart();
        }

        /// <summary>
        /// ⭐ ИСПРАВЛЕНИЕ: Проверяет все активные стрессовые дебаффы и принудительно добавляет топики
        /// Это решает проблему повторного применения дебаффа, когда топик не добавляется
        /// </summary>
        private void CheckAllStressDebuffsAndAddTopics()
        {
            // Маппинг баффов на топики (из TreatmentService)
            var buffToTopicMap = new Dictionary<string, (string topic, int days)>
            {
                [BuffIds.Tired] = (TopicIds.StressTired, 2),
                [BuffIds.Lonely] = (TopicIds.StressLonely, 2),
                [BuffIds.Thunder] = (TopicIds.StressThunder, 1),
                [BuffIds.Hunger] = (TopicIds.StressHunger, 1),
                [BuffIds.Overwork] = (TopicIds.StressOverwork, 4),
                [BuffIds.NoSleep] = (TopicIds.StressNoSleep, 1),
                [BuffIds.TooCold] = (TopicIds.StressTooCold, 1),
                [BuffIds.Social] = (TopicIds.StressSocial, 1)
            };

            foreach (var (buffId, topicData) in buffToTopicMap)
            {
                // Если бафф активен, но топика нет - добавляем принудительно
                if (_stateService.HasActiveTreatmentState(buffId) && !ConversationHelper.HasTopic(topicData.topic))
                {
                    ConversationHelper.AddTopic(topicData.topic, topicData.days);
                    _monitor.Log($"[HandleHarveyDialogue] ✅ Принудительно добавлен топик {topicData.topic} для активного баффа {buffId}", LogLevel.Info);
                }
            }
        }

        private void HandleHarveyDialogueEnd()
        {
            _monitor.Log($"[Диалог] Завершен разговор с Харви", LogLevel.Info);

            // ⭐ НОВОЕ: Не засчитываем разговоры во время событий
            if (Game1.CurrentEvent != null)
            {
                _monitor.Log($"[Диалог] Разговор во время события - не засчитывается", LogLevel.Debug);
                _stressDialogueService.ClearPendingTreatment();
                return;
            }

            // Programmatic stress-диалог: StartTreatment только после явного согласия (#$y)
            _stressDialogueService.CheckAndStartTreatmentAfterDialogue();

            // ⭐ НОВОЕ: Проверяем топики начала лечения (устанавливаются в диалогах через #$t)
            CheckTreatmentStartTopics();

            // Обновляем прогресс Social квеста
            UpdateSocialQuestProgress(showUiMessage: false);

            if (!ConversationHelper.HasTopic(TopicIds.SpokeToday))
            {
                ConversationHelper.AddTopic(TopicIds.SpokeToday, 1);
            }
        }

        /// <summary>
        /// ⭐ НОВОЕ: Проверяет топики начала лечения и запускает соответствующее лечение
        /// Топики устанавливаются в диалогах через #$t topicStressTreatmentXXXStarted 0
        /// </summary>
        private void CheckTreatmentStartTopics()
        {
            // Social - топик устанавливается в диалоге "topicStressSocial"
            if (ConversationHelper.HasTopic("topicStressTreatmentSocialStarted") && _stateService.HasActiveTreatmentState(BuffIds.Social))
            {
                _treatmentService.StartTreatment(BuffIds.Social, "Социальная тревожность");
                ConversationHelper.RemoveTopic("topicStressTreatmentSocialStarted");
                ConversationHelper.RemoveTopic(TopicIds.StressSocial);
                _monitor.Log($"[Treatment Start] Начато лечение Social (через топик)", LogLevel.Info);
            }

            // Tired - топик устанавливается в диалоге "topicStressTired"
            if (ConversationHelper.HasTopic("topicStressTreatmentTiredStarted") && _stateService.HasActiveTreatmentState(BuffIds.Tired))
            {
                _treatmentService.StartTreatment(BuffIds.Tired, "Усталость");
                ConversationHelper.RemoveTopic("topicStressTreatmentTiredStarted");
                ConversationHelper.RemoveTopic(TopicIds.StressTired);
                _monitor.Log($"[Treatment Start] Начато лечение Tired (через топик)", LogLevel.Info);
            }

            // Thunder - топик устанавливается в диалоге "topicStressThunder"
            if (ConversationHelper.HasTopic("topicStressTreatmentThunderStarted") && _stateService.HasActiveTreatmentState(BuffIds.Thunder))
            {
                _treatmentService.StartTreatment(BuffIds.Thunder, "Страх грозы");
                ConversationHelper.RemoveTopic("topicStressTreatmentThunderStarted");
                ConversationHelper.RemoveTopic(TopicIds.StressThunder);
                _monitor.Log($"[Treatment Start] Начато лечение Thunder (через топик)", LogLevel.Info);
            }

            // Hunger - топик устанавливается в диалоге "topicStressHunger"
            if (ConversationHelper.HasTopic("topicStressTreatmentHungerStarted") && _stateService.HasActiveTreatmentState(BuffIds.Hunger))
            {
                _treatmentService.StartTreatment(BuffIds.Hunger, "Голод");
                ConversationHelper.RemoveTopic("topicStressTreatmentHungerStarted");
                ConversationHelper.RemoveTopic(TopicIds.StressHunger);
                _monitor.Log($"[Treatment Start] Начато лечение Hunger (через топик)", LogLevel.Info);
            }

            // Overwork - топик устанавливается в диалоге "topicStressOverwork"
            if (ConversationHelper.HasTopic("topicStressTreatmentOverworkStarted") && _stateService.HasActiveTreatmentState(BuffIds.Overwork))
            {
                _treatmentService.StartTreatment(BuffIds.Overwork, "Переутомление");
                ConversationHelper.RemoveTopic("topicStressTreatmentOverworkStarted");
                ConversationHelper.RemoveTopic(TopicIds.StressOverwork);
                _monitor.Log($"[Treatment Start] Начато лечение Overwork (через топик)", LogLevel.Info);
            }

            // NoSleep - топик устанавливается в диалоге "topicStressNoSleep"
            if (ConversationHelper.HasTopic("topicStressTreatmentNoSleepStarted") && _stateService.HasActiveTreatmentState(BuffIds.NoSleep))
            {
                _treatmentService.StartTreatment(BuffIds.NoSleep, "Недосып");
                ConversationHelper.RemoveTopic("topicStressTreatmentNoSleepStarted");
                ConversationHelper.RemoveTopic(TopicIds.StressNoSleep);
                _monitor.Log($"[Treatment Start] Начато лечение NoSleep (через топик)", LogLevel.Info);
            }

            // TooCold - топик устанавливается в диалоге "topicStressTooCold"
            if (ConversationHelper.HasTopic("topicStressTreatmentTooColdStarted") && _stateService.HasActiveTreatmentState(BuffIds.TooCold))
            {
                _treatmentService.StartTreatment(BuffIds.TooCold, "Переохлаждение");
                ConversationHelper.RemoveTopic("topicStressTreatmentTooColdStarted");
                ConversationHelper.RemoveTopic(TopicIds.StressTooCold);
                _monitor.Log($"[Treatment Start] Начато лечение TooCold (через топик)", LogLevel.Info);
            }

            // Lonely - топик устанавливается в диалоге "topicStressLonely"
            if (ConversationHelper.HasTopic("topicStressTreatmentLonelyStarted") && _stateService.HasActiveTreatmentState(BuffIds.Lonely))
            {
                _treatmentService.StartTreatment(BuffIds.Lonely, "Одиночество");
                ConversationHelper.RemoveTopic("topicStressTreatmentLonelyStarted");
                ConversationHelper.RemoveTopic(TopicIds.StressLonely);
                _monitor.Log($"[Treatment Start] Начато лечение Lonely (через топик)", LogLevel.Info);
            }

            // Darkness - топик устанавливается в диалоге "topicStressDarkness" через DarknessService
            if (ConversationHelper.HasTopic("topicStressTreatmentDarknessStarted"))
            {
                // Для Darkness используется специальная система через DarknessService
                // Топик обрабатывается в CheckDarknessTherapyStart()
                _monitor.Log($"[Treatment Start] Топик topicStressTreatmentDarknessStarted обнаружен (обрабатывается в DarknessService)", LogLevel.Debug);
            }
        }

        private void HandleDialogueEnd()
        {
            // ⭐ НОВОЕ: Не засчитываем разговоры во время событий
            if (Game1.CurrentEvent != null)
            {
                _monitor.Log($"[Диалог] Разговор во время события - не засчитывается", LogLevel.Debug);
                _lastDialogueNpc = null;
                return;
            }

            if (_lastDialogueNpc != "Harvey")
            {
                if (_lastDialogueNpc != null)
                {
                    _data.TalkedNpcsToday.Add(_lastDialogueNpc);
                    _monitor.Log($"[Диалог] Завершен разговор с {_lastDialogueNpc}. Всего разговоров сегодня: {_data.TalkedNpcsToday.Count}", LogLevel.Info);
                }

                UpdateLonelyQuestProgress();
                UpdateSocialQuestProgress(showUiMessage: true);
            }
            else
            {
                _monitor.Log($"[Диалог] Завершен разговор с Харви (не учитывается в счетчике)", LogLevel.Debug);
                UpdateSocialQuestProgress(showUiMessage: false);
            }

            _lastDialogueNpc = null;

            if (!ConversationHelper.HasTopic(TopicIds.SpokeToday))
            {
                ConversationHelper.AddTopic(TopicIds.SpokeToday, 1);
            }
        }

        private void CheckSocialStressTrigger(NPC npc)
        {
            // ⭐ ИСПРАВЛЕНО: Не применяем дебафф во время событий
            if (Game1.CurrentEvent != null) return;
            
            if (Game1.stats.DaysPlayed < 5) return;
            if (_stateService.HasActiveTreatmentState(BuffIds.Social)) return;
            if (_stateService.HasImmunity(BuffIds.Social)) return;

            if (Game1.player.friendshipData.TryGetValue(npc.Name, out var friendship))
            {
                if (friendship.Points < 750 && Game1.random.NextDouble() < 0.3)
                {
                    _treatmentService.ApplyStressBuff(BuffIds.Social, "Социальный дискомфорт");
                    _monitor.Log($"[Social Stress] Триггер активирован при разговоре с {npc.Name} (дружба: {friendship.Points}/750)", LogLevel.Info);
                }
            }
        }

        private void UpdateLonelyQuestProgress()
        {
            var lonelyTreatment = GetTreatmentByQuest(QuestIds.Lonely);
            if (_stateService.HasActiveQuestState(QuestIds.Lonely) && lonelyTreatment?.Progress != null)
            {
                lonelyTreatment.Progress.TalkedUniqueToday = _data.TalkedNpcsToday.Count;

                Game1.addHUDMessage(new HUDMessage($"+1 общение ({lonelyTreatment.Progress.TalkedUniqueToday}/3)", 2));

                if (lonelyTreatment.Progress.TalkedUniqueToday >= 3)
                {
                    Game1.playSound("questcomplete");
                    _stateService.CompleteTreatment(QuestIds.Lonely);
                    ConversationHelper.AddTopic("topicStressTreatmentLonelyCured", 2);
                }
            }
        }

        private void UpdateSocialQuestProgress(bool showUiMessage = false)
        {
            if (!_data.StressState.HasActiveQuest(QuestIds.Social)) return;

            var socialTreatment = GetTreatmentByQuest(QuestIds.Social);
            if (socialTreatment?.Progress == null) return;

            int baseConversations = socialTreatment.Progress.TalkedUniqueToday;
            int currentTotal = _data.TalkedNpcsToday.Count;
            int conversationsAfterQuest = Math.Max(0, currentTotal - baseConversations);

            if (socialTreatment.Progress.SocialTalksAfterQuest != conversationsAfterQuest)
            {
                socialTreatment.Progress.SocialTalksAfterQuest = conversationsAfterQuest;
                _triggerService.UpdateQuestDescription(socialTreatment.Progress);

                // Показываем сообщение о прогрессе только если запрошено (при завершении диалогов)
                if (showUiMessage)
                {
                    string progressText = socialTreatment.Progress.GetSocialProgressText();
                    Game1.addHUDMessage(new HUDMessage(progressText, HUDMessage.newQuest_type));
                }
            }
        }

        private void ApplyThunderCalmingBuff(bool harveyNearby)
        {
            bool shouldApply = harveyNearby
                && Game1.player.currentLocation?.NameOrUniqueName == "Hospital"
                && (Game1.isLightning || Game1.isRaining);

            if (shouldApply)
            {
                if (!_buffService.HasBuff(BuffIds.CalmingAtHospital))
                {
                    _buffService.ApplyBuff(BuffIds.CalmingAtHospital, "Успокоение с Харви",
                        new StardewValley.Buffs.BuffEffects { }, -2);
                }
            }
            else if (_buffService.HasBuff(BuffIds.CalmingAtHospital))
            {
                _buffService.RemoveBuff(BuffIds.CalmingAtHospital);
            }
        }

        /// <summary>
        /// ⭐ УЛУЧШЕНО: Обрабатывает потребление еды игроком
        /// Вызывается из FoodConsumptionPatch после Farmer.doneEating
        /// </summary>
        public void OnFoodConsumed(StardewValley.Object consumed)
        {
            _monitor.Log($"[FoodConsumption] Игрок съел: {consumed.DisplayName ?? consumed.Name} ({consumed.QualifiedItemId})", LogLevel.Debug);

            // Топик «ел сегодня» — только для счётчика дней без еды, не блокирует квесты
            if (!ConversationHelper.HasTopic(TopicIds.AteToday))
                ConversationHelper.AddTopic(TopicIds.AteToday, 1);

            var activeHunger = GetTreatmentByQuest(QuestIds.Hunger)
                ?? _data.StressState.GetActiveTreatment(BuffIds.Hunger);

            // Завершаем Hunger квест если активен (лечение уже начато)
            if (_stateService.HasActiveQuestState(QuestIds.Hunger))
            {
                if (activeHunger?.Progress != null)
                    activeHunger.Progress.AteAnyFood = true;

                Game1.playSound("questcomplete");
                _stateService.CompleteTreatment(QuestIds.Hunger);
                ConversationHelper.AddTopic("topicStressTreatmentHungerCured", 2);
            }
            else if (activeHunger != null && !activeHunger.TreatmentStarted)
            {
                // M-02: естественное решение до разговора с Harvey — полная очистка state
                _monitor.Log("[FoodConsumption] Hunger resolved naturally before treatment started", LogLevel.Info);

                _buffService.RemoveBuff(BuffIds.Hunger);
                ConversationHelper.RemoveTopic(TopicIds.StressHunger);
                ConversationHelper.RemoveTopic(TopicIds.TreatmentStartHunger);

                activeHunger.IsCured = true;
                activeHunger.CompletedDate = SDate.Now();

                if (_data.StressState.RemoveTreatment(activeHunger.TreatmentKey))
                {
                    _monitor.Log($"[FoodConsumption] ActiveTreatment removed: {activeHunger.TreatmentKey}", LogLevel.Debug);
                }
                else
                {
                    _monitor.Log($"[FoodConsumption] Failed to remove ActiveTreatment: {activeHunger.TreatmentKey}", LogLevel.Warn);
                }

                Game1.addHUDMessage(new HUDMessage("Голод утолён", HUDMessage.newQuest_type));
            }
            else if (_stateService.HasBuffInGame(BuffIds.Hunger))
            {
                // Edge case: game buff без TreatmentState
                _buffService.RemoveBuff(BuffIds.Hunger);
                Game1.addHUDMessage(new HUDMessage("Голод утолён", HUDMessage.newQuest_type));
            }

            // Завершаем TooCold квест если активен — только горячий напиток (C-08)
            if (_stateService.HasActiveQuestState(QuestIds.TooCold))
            {
                if (HotDrinkHelper.IsHotDrinkOrWarmingFood(consumed))
                {
                    Game1.playSound("questcomplete");
                    _stateService.CompleteTreatment(QuestIds.TooCold);
                    ConversationHelper.AddTopic("topicStressTreatmentTooColdCured", 2);
                }
                else
                {
                    _monitor.Log(
                        $"[FoodConsumption] TooCold: {consumed.QualifiedItemId} is not a hot drink — quest not completed",
                        LogLevel.Debug);
                    Game1.addHUDMessage(new HUDMessage("Нужно что-то горячее: чай или кофе", HUDMessage.newQuest_type));
                }
            }
        }

        private void NaturalBuffRemoval(bool harveyNearby)
        {
            // Tired - rest at home late evening
            if (CanNaturallyRemoveDebuff(BuffIds.Tired)
                && Game1.player.currentLocation is StardewValley.Locations.FarmHouse
                && GameStateHelper.IsEveningNight(2200, 2600))
            {
                NaturallyResolveBeforeTreatment(BuffIds.Tired, TopicIds.StressTired, TopicIds.TreatmentStartTired);
            }

            // Lonely - removal when talking to Harvey
            if (CanNaturallyRemoveDebuff(BuffIds.Lonely) && harveyNearby)
            {
                NaturallyResolveBeforeTreatment(
                    BuffIds.Lonely,
                    TopicIds.StressLonely,
                    TopicIds.TreatmentStartLonely,
                    () => Game1.getCharacterFromName("Harvey")?.showTextAboveHead("Я всегда рядом."));
            }

            // Thunder - removal indoors with Harvey
            if (CanNaturallyRemoveDebuff(BuffIds.Thunder)
                && harveyNearby
                && Game1.player.currentLocation?.NameOrUniqueName == "Hospital"
                && (Game1.isLightning || Game1.isRaining))
            {
                NaturallyResolveBeforeTreatment(BuffIds.Thunder, TopicIds.StressThunder, TopicIds.TreatmentStartThunder);
            }

            // TooCold - removal in warm zones
            if (CanNaturallyRemoveDebuff(BuffIds.TooCold)
                && GameStateHelper.IsInWarmZone())
            {
                NaturallyResolveBeforeTreatment(BuffIds.TooCold, TopicIds.StressTooCold, TopicIds.TreatmentStartTooCold);
            }

            // Hunger — OnFoodConsumed (M-02). Darkness — DarknessService therapy (не менять).
        }

        /// <summary>
        /// Natural removal только до StartTreatment: реальный game buff + лечение не начато.
        /// </summary>
        private bool CanNaturallyRemoveDebuff(string buffId)
        {
            if (!_stateService.HasBuffInGame(buffId))
                return false;

            var activeTreatment = _data.StressState.GetActiveTreatment(buffId);
            return activeTreatment == null || !activeTreatment.TreatmentStarted;
        }

        /// <summary>
        /// Снимает debuff естественно до начала лечения: buff, topics, ActiveTreatment, history.
        /// Не завершает квест и не выдаёт награды.
        /// </summary>
        private void NaturallyResolveBeforeTreatment(
            string buffId,
            string stressTopic,
            string? treatmentStartTopic = null,
            Action? afterRemove = null)
        {
            var activeTreatment = _data.StressState.GetActiveTreatment(buffId);

            _monitor.Log($"[NaturalBuffRemoval] {buffId} naturally resolved before treatment started", LogLevel.Info);

            _buffService.RemoveBuff(buffId);
            ConversationHelper.RemoveTopic(stressTopic);
            if (!string.IsNullOrEmpty(treatmentStartTopic))
                ConversationHelper.RemoveTopic(treatmentStartTopic);

            if (activeTreatment != null)
            {
                activeTreatment.IsCured = true;
                activeTreatment.CompletedDate = SDate.Now();

                if (!_data.StressState.RemoveTreatment(activeTreatment.TreatmentKey))
                {
                    _monitor.Log(
                        $"[NaturalBuffRemoval] Failed to remove ActiveTreatment: {activeTreatment.TreatmentKey}",
                        LogLevel.Warn);
                }
            }

            afterRemove?.Invoke();
        }

        // УСТАРЕВШИЙ МЕТОД - Теперь используется DarknessService.CheckAndApplyDarknessBuff
        // Оставлен для обратной совместимости, но больше не вызывается
        [Obsolete("Используйте DarknessService.CheckAndApplyDarknessBuff")]
        private void CheckDarknessDebuff(GameLocation newLocation)
        {
            // Старая логика закомментирована - используйте DarknessService
            /*
            if (Game1.stats.DaysPlayed >= 3
                && Game1.timeOfDay >= 2200 && Game1.timeOfDay <= 2600
                && !_stateService.HasActiveTreatmentState(BuffIds.Darkness)
                && !_stateService.HasImmunity(BuffIds.Darkness))
            {
                var n = newLocation.NameOrUniqueName;
                if (n == "Backwoods" || n == "Forest" || n == "Mountain")
                {
                    _treatmentService.ApplyStressBuff(BuffIds.Darkness, "Темнота");
                }
            }
            */
        }

        private void ApplyQuestLocationBuffs(GameLocation newLocation)
        {
            // Tired quest - resting at home buff
            if (_stateService.HasActiveQuestState(QuestIds.Tired)
                && newLocation is StardewValley.Locations.FarmHouse
                && !GameStateHelper.HasHeavyTools(Game1.player)
                && !_buffService.HasBuff(BuffIds.RestingAtHome))
            {
                _buffService.ApplyBuff(BuffIds.RestingAtHome, "Отдых дома",
                    new StardewValley.Buffs.BuffEffects { }, -2);
            }

            ManageOverworkBreaks(newLocation);

            // Darkness quest - light and safety buff
            if (_stateService.HasActiveQuestState(QuestIds.Darkness)
                && GameStateHelper.IsEveningNight(2000, 2600)
                && !_buffService.HasBuff(BuffIds.LightAndSafe)
                && newLocation is StardewValley.Locations.FarmHouse)
            {
                _buffService.ApplyBuff(BuffIds.LightAndSafe, "Свет и безопасность",
                    new StardewValley.Buffs.BuffEffects { }, -2);
            }
        }

        private void ManageOverworkBreaks(GameLocation newLocation)
        {
            if (!_stateService.HasActiveQuestState(QuestIds.Overwork)) return;

            bool restZone = GameStateHelper.IsInRestZone();

            if (restZone && _data.OverworkBreaksToday < 3 && !_buffService.HasBuff(BuffIds.OverworkBreak))
            {
                _buffService.ApplyBuff(BuffIds.OverworkBreak, "Перерыв",
                    new StardewValley.Buffs.BuffEffects { }, -2);
                ConversationHelper.AddTopic(TopicIds.OverworkBreakActive, 1);
                ConversationHelper.RemoveTopic(TopicIds.OverworkBreakInterrupted);
                Game1.playSound("sipTea");
            }
            else if (!restZone && _buffService.HasBuff(BuffIds.OverworkBreak))
            {
                _buffService.RemoveBuff(BuffIds.OverworkBreak);
                _data.OverworkBreakSeconds = 0;
                _data.OverworkBreakActive = false;
                ConversationHelper.AddTopic(TopicIds.OverworkBreakInterrupted, 0);
                ConversationHelper.RemoveTopic(TopicIds.OverworkBreakActive);
                Game1.playSound("cancel");
            }
        }

        private void CheckLightningStressTrigger()
        {
            if (Game1.stats.DaysPlayed < 2) return;
            if (_stateService.HasActiveTreatmentState(BuffIds.Thunder)) return;
            if (_stateService.HasImmunity(BuffIds.Thunder)) return;

            if (Game1.random.NextDouble() < 0.3)
            {
                _treatmentService.ApplyStressBuff(BuffIds.Thunder, "Страх грозы");
            }
        }

        /// <summary>
        /// Выдача debuff TooCold: вечер/ночь (21:00–2:00), холодная погода, холодные локации.
        /// Вызывается при warp и при переходе времени за 21:00 (не на DayStarted).
        /// </summary>
        private void TryApplyTooColdStressTrigger()
        {
            if (Game1.stats.DaysPlayed < 2) return;
            if (!GameStateHelper.IsEveningNight(2100, 2600)) return;
            if (!GameStateHelper.IsSeasonOneOf("spring", "fall", "winter")) return;
            if (!GameStateHelper.IsWeatherOneOf("Snow", "Rain", "Wind", "Storm")) return;
            if (_stateService.HasActiveTreatmentState(BuffIds.TooCold)) return;
            if (_stateService.HasImmunity(BuffIds.TooCold)) return;

            var loc = Game1.player.currentLocation?.NameOrUniqueName;
            if (loc is "Mountain" or "Forest" or "Railroad" or "Backwoods")
            {
                _treatmentService.ApplyStressBuff(BuffIds.TooCold, "Переохлаждение");
            }
        }

        /// <summary>
        /// ⭐ ИСПРАВЛЕНО: Проверяет усталость в течение дня
        /// Вызывается из ProcessGameTick, когда stamina падает до низкого уровня
        /// </summary>
        private void CheckTiredStressTrigger()
        {
            // Проверяем только раз в минуту (каждые 60 тиков) для оптимизации
            if (Game1.ticks % 60 != 0) return;

            // Базовые проверки
            if (Game1.stats.DaysPlayed < 1) return;
            if (_stateService.HasImmunity(BuffIds.Tired)) return;
            if (_stateService.HasActiveTreatmentState(BuffIds.Tired)) return;

            // Проверяем, что stamina низкая (<= 10) и игрок не в событии
            if (Game1.CurrentEvent != null) return;
            
            if (Game1.player.Stamina >= 0 && Game1.player.Stamina <= 10)
            {
                _treatmentService.ApplyStressBuff(BuffIds.Tired, "Усталость");
                _monitor.Log($"[Tired Stress] Триггер активирован: stamina={Game1.player.Stamina}/270", LogLevel.Info);
            }
        }

        /// <summary>
        /// ⭐ НОВОЕ: Обновляет счетчики дней без разговоров/еды в начале нового дня
        /// Вызывается ПЕРЕД очисткой топиков
        /// </summary>
        private void UpdateConsecutiveDaysCounters()
        {
            // Проверяем, разговаривал ли игрок вчера (топик SpokeToday НЕ истекает в конце дня)
            bool spokeYesterday = ConversationHelper.HasTopic(TopicIds.SpokeToday);
            
            if (spokeYesterday)
            {
                // Разговаривал - сбрасываем счетчик
                _data.DaysWithoutTalking = 0;
                _monitor.Log("[DaysCounter] Игрок разговаривал вчера - счетчик Lonely сброшен", LogLevel.Debug);
            }
            else
            {
                // Не разговаривал - увеличиваем счетчик
                _data.DaysWithoutTalking++;
                _monitor.Log($"[DaysCounter] Игрок НЕ разговаривал вчера - счетчик Lonely: {_data.DaysWithoutTalking} дней", LogLevel.Info);
            }

            // Проверяем, ел ли игрок вчера (топик AteToday НЕ истекает в конце дня)
            bool ateYesterday = ConversationHelper.HasTopic(TopicIds.AteToday);
            
            if (ateYesterday)
            {
                // Ел - сбрасываем счетчик
                _data.DaysWithoutEating = 0;
                _monitor.Log("[DaysCounter] Игрок ел вчера - счетчик Hunger сброшен", LogLevel.Debug);
            }
            else
            {
                // Не ел - увеличиваем счетчик
                _data.DaysWithoutEating++;
                _monitor.Log($"[DaysCounter] Игрок НЕ ел вчера - счетчик Hunger: {_data.DaysWithoutEating} дней", LogLevel.Info);
            }
        }

        private void ResetDailyQuestCounters()
        {
            if (_stateService.HasActiveQuestState(QuestIds.Overwork))
            {
                _data.OverworkBreaksToday = 0;
                _buffService.RemoveBuff(BuffIds.OverworkBreak);
                ConversationHelper.RemoveTopic(TopicIds.OverworkBreakActive);
                ConversationHelper.RemoveTopic(TopicIds.OverworkBreakInterrupted);
            }

            if (_stateService.HasActiveQuestState(QuestIds.Thunder))
            {
                var thunderTreatment = GetTreatmentByQuest(QuestIds.Thunder);
                if (thunderTreatment?.Progress != null)
                {
                    thunderTreatment.Progress.SecondsNearHarvey = 0;
                }
            }

            var lonelyTreatment = GetTreatmentByQuest(QuestIds.Lonely);
            if (lonelyTreatment?.Progress != null)
            {
                lonelyTreatment.Progress.TalkedUniqueToday = 0;
            }

            // ⭐ НОВОЕ: Для Social квеста НЕ сбрасываем TalkedUniqueToday!
            // Это базовое значение разговоров на момент получения квеста
            // Сбрасываем только счетчик разговоров ПОСЛЕ квеста и время с Харви
            var socialTreatment = GetTreatmentByQuest(QuestIds.Social);
            if (socialTreatment?.Progress != null)
            {
                socialTreatment.Progress.SocialTalksAfterQuest = 0;  // Сбрасываем счетчик после квеста
                socialTreatment.Progress.SecondsNearHarvey = 0;       // Сбрасываем время с Харви
                // TalkedUniqueToday НЕ трогаем - это база!
            }
        }

        private TreatmentState? GetTreatmentByQuest(string questId)
        {
            return _data.StressState.GetActiveTreatmentByQuest(questId);
        }

        // ===== МЕТОДЫ ДЛЯ ТЕРАПИИ СТРАХА ТЕМНОТЫ =====

        /// <summary>
        /// Проверить наличие дебаффов темноты и добавить соответствующие топики
        /// </summary>
        private void CheckDarknessDebuffsAndAddTopics()
        {
            // Проверяем, не идёт ли уже терапия
            if (_data.Darkness.IsTherapyActive) return;

            // Проверяем уровень 3 (фобия) - приоритет
            if (_stateService.HasBuffInGame("buffDarknessLevel3"))
            {
                if (!ConversationHelper.HasTopic("topicStressDarknessLevel3"))
                {
                    ConversationHelper.AddTopic("topicStressDarknessLevel3", 0); // Не истекает
                    _monitor.Log("[DarknessTherapy] Добавлен топик для Уровня 3 (фобия)", LogLevel.Info);
                }
                return; // Не проверяем дальше
            }

            // Проверяем уровень 2 (сильный страх)
            if (_stateService.HasActiveTreatmentState("buffDarknessLevel2"))
            {
                if (!ConversationHelper.HasTopic("topicStressDarknessLevel2"))
                {
                    ConversationHelper.AddTopic("topicStressDarknessLevel2", 7); // 7 дней
                    _monitor.Log("[DarknessTherapy] Добавлен топик для Уровня 2 (сильный страх)", LogLevel.Info);
                }
                return;
            }

            // Проверяем уровень 1 (легкий страх) - старый топик уже должен быть
            if (_stateService.HasBuffInGame("buffDarknessLevel1") || _stateService.HasBuffInGame(BuffIds.Darkness))
            {
                if (!ConversationHelper.HasTopic(TopicIds.StressDarkness))
                {
                    ConversationHelper.AddTopic(TopicIds.StressDarkness, 7); // 7 дней
                    _monitor.Log("[DarknessTherapy] Добавлен топик для Уровня 1 (легкий страх)", LogLevel.Info);
                }
            }
        }

        /// <summary>
        /// Проверить топик начала терапии и запустить терапию
        /// </summary>
        private void CheckDarknessTherapyStart()
        {
            // Если топик начала терапии активен и терапия еще не начата
            if (ConversationHelper.HasTopic("topicDarknessTherapyStart") && !_data.Darkness.IsTherapyActive)
            {
                // Запускаем терапию
                _darknessService.StartTherapy();
                
                // Добавляем квест первого шага
                var quest = Game1.player.questLog.FirstOrDefault(q => q.id.Value == "HarveyMod_DarknessStep1");
                if (quest == null)
                {
                    Game1.player.addQuest("HarveyMod_DarknessStep1");
                    _monitor.Log("[DarknessTherapy] Добавлен квест Шага 1", LogLevel.Info);
                }
                
                // Удаляем топики уровней страха
                ConversationHelper.RemoveTopic(TopicIds.StressDarkness);
                ConversationHelper.RemoveTopic("topicStressDarknessLevel2");
                ConversationHelper.RemoveTopic("topicStressDarknessLevel3");
                
                _monitor.Log("[DarknessTherapy] ✅ Терапия начата через диалог!", LogLevel.Info);
            }
        }

    }
}
