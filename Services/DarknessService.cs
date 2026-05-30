using System;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using HarveyStressMeter.Models;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;

namespace HarveyStressMeter.Services
{
    /// <summary>
    /// Сервис для управления дебаффом "Боязнь темноты" с системой уровней
    /// </summary>
    public class DarknessService
    {
        private readonly SaveData _data;
        private readonly BuffService _buffService;
        private readonly StateService _stateService;
        private readonly QuestService _questService;
        private StressLoadService? _stressLoadService;
        private readonly IMonitor _monitor;

        private bool _worryLetterSent;

        public DarknessService(
            SaveData data,
            BuffService buffService,
            StateService stateService,
            QuestService questService,
            IMonitor monitor)
        {
            _data = data;
            _buffService = buffService;
            _stateService = stateService;
            _questService = questService;
            _monitor = monitor;
        }

        public void SetStressLoadService(StressLoadService stressLoadService)
            => _stressLoadService = stressLoadService;

        /// <summary>
        /// Проверить и применить дебафф темноты при входе в локацию
        /// </summary>
        public void CheckAndApplyDarknessBuff(GameLocation location)
        {
            // Базовые проверки
            if (Game1.stats.DaysPlayed < 3) return;
            if (Game1.timeOfDay < 2200 || Game1.timeOfDay > 2600) return;
            if (_stateService.HasImmunity(BuffIds.Darkness)) return;
            
            // Проверяем, излечен ли игрок полностью
            if (_data.Darkness.IsCured && _data.Darkness.HasOvercomeBonus)
            {
                _monitor.Log("[DarknessService] Игрок полностью излечен от страха темноты", LogLevel.Debug);
                return;
            }

            // Проверяем темные локации
            var locationName = location.NameOrUniqueName;
            bool isDarkLocation = locationName == "Backwoods" || locationName == "Forest" || locationName == "Mountain";
            
            if (!isDarkLocation) return;

            // Регистрируем эпизод страха
            RegisterDarknessEpisode();
            
            // Применяем дебафф в зависимости от уровня страха
            ApplyFearLevelBuff();
        }

        /// <summary>
        /// Зарегистрировать эпизод страха темноты
        /// </summary>
        private void RegisterDarknessEpisode()
        {
            var currentDate = SDate.Now();
            _data.Darkness.RegisterEpisode(currentDate);
            
            _monitor.Log($"[DarknessService] Эпизод страха зарегистрирован. Всего: {_data.Darkness.DarknessEpisodesCount}, за неделю: {_data.Darkness.EpisodesThisWeek}", LogLevel.Info);

            // Если это первый эпизод, повышаем уровень до 1
            if (_data.Darkness.FearLevel == 0)
            {
                _data.Darkness.IncreaseFearLevel(currentDate);
                _monitor.Log("[DarknessService] Первый эпизод страха! Уровень повышен до 1", LogLevel.Info);
                
                // Добавляем топик для диалога с Харви
                ConversationHelper.AddTopic(TopicIds.StressDarkness, 7); // 7 дней на разговор
            }
        }

        /// <summary>
        /// Применить бафф в зависимости от текущего уровня страха
        /// </summary>
        private void ApplyFearLevelBuff()
        {
            string buffId = GetCurrentBuffId();
            string displayName = GetFearLevelDisplayName();
            
            // Проверяем, уже активен ли бафф этого уровня
            if (_stateService.HasBuffInGame(buffId))
            {
                _monitor.Log($"[DarknessService] Бафф {buffId} уже активен", LogLevel.Debug);
                return;
            }

            // Удаляем старые баффы других уровней
            RemoveOtherFearLevelBuffs(buffId);

            // Применяем бафф
            var effects = GetFearLevelEffects(_data.Darkness.FearLevel);
            _buffService.ApplyBuff(buffId, displayName, effects, Buff.ENDLESS);
            
            _monitor.Log($"[DarknessService] Применен бафф уровня {_data.Darkness.FearLevel}: {buffId} ({displayName})", LogLevel.Info);
            
            // Показываем сообщение игроку
            ShowFearLevelMessage();
            SyncStressLoadFromDarkness();
        }

        /// <summary>Legacy buffStressDarkness → уровневая система; снятие устаревшего квеста.</summary>
        public void MigrateLegacyDarknessPath()
        {
            if (!DarknessLegacyHelper.UsesLevelSystem(_data, _stateService))
            {
                if (_stateService.HasBuffInGame(BuffIds.Darkness) && _data.Darkness.FearLevel == 0)
                {
                    _data.Darkness.IncreaseFearLevel(SDate.Now());
                    _buffService.RemoveBuff(BuffIds.Darkness);
                    ApplyFearLevelBuff();
                    ConversationHelper.RemoveTopic(TopicIds.StressDarkness);
                    _monitor.Log("[DarknessService] Миграция legacy buffStressDarkness → уровень 1", LogLevel.Info);
                }

                return;
            }

            if (_stateService.HasBuffInGame(BuffIds.Darkness))
                _buffService.RemoveBuff(BuffIds.Darkness);

            if (_stateService.HasActiveQuestState(QuestIds.Darkness))
            {
                _questService.CompleteQuest(QuestIds.Darkness);
                Game1.player.removeQuest(QuestIds.Darkness);
                _buffService.RemoveBuff(BuffIds.LightAndSafe);
                _monitor.Log("[DarknessService] Снят legacy-квест HarveyMod_DarknessRecovery (активна терапия уровней)", LogLevel.Info);
            }
        }

        private void SyncStressLoadFromDarkness()
            => _stressLoadService?.SyncFromGameState();

        /// <summary>Сброс зависших топиков/флагов после прерванного старта терапии.</summary>
        public void CleanupStaleDarknessState()
        {
            if (!_data.Darkness.IsTherapyActive && ConversationHelper.HasTopic("topicDarknessTherapyStart"))
            {
                ConversationHelper.RemoveTopic("topicDarknessTherapyStart");
                _monitor.Log("[DarknessService] Удалён устаревший topicDarknessTherapyStart (терапия не активна)", LogLevel.Info);
            }

            const string step1QuestId = "HarveyMod_DarknessStep1";
            if (_data.Darkness.IsTherapyActive
                && !_questService.HasQuest(step1QuestId)
                && _data.Darkness.TherapyStage <= 1
                && _data.Darkness.SafeDarknessMinutes == 0)
            {
                _data.Darkness.IsTherapyActive = false;
                _data.Darkness.TherapyStage = 0;
                _data.Darkness.TherapyStartDate = null;
                ConversationHelper.RemoveTopic("topicDarknessTherapyStart");
                _monitor.Log("[DarknessService] Сброшен некonsistentный флаг терапии без квеста в журнале", LogLevel.Warn);
            }
        }

        /// <summary>
        /// Обновить состояние страха темноты (вызывается каждый день)
        /// </summary>
        public void UpdateDailyFearState()
        {
            var currentDate = SDate.Now();

            CleanupStaleDarknessState();
            
            // Обновляем счетчик игнорирования
            _data.Darkness.UpdateIgnoredDays(currentDate);
            
            _monitor.Log($"[DarknessService] Ежедневное обновление: Уровень={_data.Darkness.FearLevel}, Игнорируется={_data.Darkness.DaysIgnored} дней", LogLevel.Info);

            MigrateLegacyDarknessPath();
            // Проверяем, нужно ли повысить уровень страха
            if (_data.Darkness.ShouldIncreaseFearLevel(currentDate))
            {
                IncreaseFearLevel();
            }
            
            // Проверяем естественное снижение (только для уровня 1)
            if (_data.Darkness.CanDecreaseFearNaturally(currentDate))
            {
                DecreaseFearLevel();
            }
            
            // Применяем бафф если страх активен
            if (_data.Darkness.FearLevel > 0)
            {
                ApplyFearLevelBuff();
            }
        }

        /// <summary>
        /// Повысить уровень страха
        /// </summary>
        private void IncreaseFearLevel()
        {
            int oldLevel = _data.Darkness.FearLevel;
            _data.Darkness.IncreaseFearLevel(SDate.Now());
            int newLevel = _data.Darkness.FearLevel;
            
            _monitor.Log($"[DarknessService] ⚠️ Уровень страха повышен: {oldLevel} → {newLevel}", LogLevel.Warn);
            
            // Удаляем старый топик и добавляем новый
            ConversationHelper.RemoveTopic(TopicIds.StressDarkness);
            
            if (newLevel == 2)
            {
                ConversationHelper.AddTopic("topicStressDarknessSerious", 7);
                Game1.addHUDMessage(new HUDMessage("Страх темноты усиливается...", HUDMessage.error_type));
            }
            else if (newLevel == 3)
            {
                ConversationHelper.AddTopic("topicStressDarknessPhobia", 0); // Не истекает, пока не начнется лечение
                Game1.addHUDMessage(new HUDMessage("Страх темноты перерос в фобию!", HUDMessage.error_type));
                
                // Отправляем письмо от Харви
                SendHarveyWorryLetter();
            }
            
            // Применяем новый бафф
            ApplyFearLevelBuff();
        }

        /// <summary>
        /// Снизить уровень страха естественным путем
        /// </summary>
        private void DecreaseFearLevel()
        {
            int oldLevel = _data.Darkness.FearLevel;
            _data.Darkness.DecreaseFearLevel();
            int newLevel = _data.Darkness.FearLevel;
            
            _monitor.Log($"[DarknessService] ✅ Уровень страха снижен естественным путем: {oldLevel} → {newLevel}", LogLevel.Info);
            
            if (newLevel == 0)
            {
                // Полностью снят
                RemoveAllFearBuffs();
                ConversationHelper.RemoveTopic(TopicIds.StressDarkness);
                Game1.addHUDMessage(new HUDMessage("Страх темноты прошел.", HUDMessage.achievement_type));
            }
        }

        /// <summary>
        /// Начать терапию страха темноты
        /// </summary>
        public void StartTherapy()
        {
            if (_data.Darkness.IsTherapyActive)
            {
                _monitor.Log("[DarknessService] Терапия уже активна", LogLevel.Warn);
                return;
            }

            _data.Darkness.IsTherapyActive = true;
            _data.Darkness.TherapyStartDate = SDate.Now();
            _data.Darkness.TherapyStage = 1; // Начинаем с Шага 1
            
            _monitor.Log($"[DarknessService] ✅ Терапия начата! Текущий уровень страха: {_data.Darkness.FearLevel}", LogLevel.Info);
            
            // Удаляем топики игнорирования и согласия на терапию
            ConversationHelper.RemoveTopic(TopicIds.StressDarkness);
            ConversationHelper.RemoveTopic("topicStressDarknessSerious");
            ConversationHelper.RemoveTopic("topicStressDarknessPhobia");
            ConversationHelper.RemoveTopic("topicDarknessTherapyStart");
            ConversationHelper.RemoveTopic("topicStressDarknessLevel2");
            ConversationHelper.RemoveTopic("topicStressDarknessLevel3");
            
            // Сбрасываем счетчик игнорирования
            _data.Darkness.IgnoredSinceDate = null;
            _data.Darkness.DaysIgnored = 0;

            const string step1QuestId = "HarveyMod_DarknessStep1";
            if (!_questService.HasQuest(step1QuestId))
                _questService.AddQuest(step1QuestId);
            
            Game1.playSound("newRecipe");
            Game1.addHUDMessage(new HUDMessage("Терапия страха темноты началась", HUDMessage.newQuest_type));
            RefreshTherapyQuestJournal(1);
        }

        /// <summary>
        /// Завершить терапию полностью
        /// </summary>
        public void CompleteTherapy()
        {
            _data.Darkness.IsCured = true;
            _data.Darkness.HasOvercomeBonus = true;
            _data.Darkness.TherapyCompletedDate = SDate.Now();
            _data.Darkness.FearLevel = 0;
            _data.Darkness.IsTherapyActive = false;
            
            _monitor.Log("[DarknessService] ✅✅✅ Терапия полностью завершена! Игрок излечен!", LogLevel.Info);
            
            // Удаляем все дебаффы страха
            RemoveAllFearBuffs();
            
            // Применяем перманентный бонус "Преодоление"
            ApplyOvercomeBonus();
            
            Game1.playSound("yoba");
            Game1.addHUDMessage(new HUDMessage("Ты преодолела страх темноты!", HUDMessage.achievement_type));
            
            // +200 дружбы с Харви
            var harvey = Game1.getCharacterFromName("Harvey");
            if (harvey != null)
            {
                Game1.player.changeFriendship(200, harvey);
            }
        }

        /// <summary>
        /// Проверить, можно ли выходить из дома ночью (для Уровня 3)
        /// </summary>
        public bool CanLeaveHouseAtNight()
        {
            // Если уровень страха 3 и время после 22:00
            if (_data.Darkness.FearLevel >= 3 && Game1.timeOfDay >= 2200)
            {
                // Проверяем, не активна ли терапия на Шаге 2 или 3
                if (_data.Darkness.IsTherapyActive && _data.Darkness.TherapyStage >= 2)
                {
                    return true; // Во время терапии можно выходить
                }
                
                return false; // Фобия блокирует выход
            }
            
            return true;
        }

        /// <summary>
        /// Показать сообщение о блокировке выхода
        /// </summary>
        public void ShowCannotLeaveMessage()
        {
            Game1.drawObjectDialogue("Ты не можешь заставить себя выйти... Слишком темно и страшно...");
            Game1.playSound("cancel");
        }

        // ===== МЕХАНИКИ ШАГОВ ТЕРАПИИ =====

        /// <summary>
        /// Обновить прогресс терапии (вызывается каждый тик игры)
        /// </summary>
        public void UpdateTherapyProgress()
        {
            if (!_data.Darkness.IsTherapyActive) return;

            switch (_data.Darkness.TherapyStage)
            {
                case 1:
                    UpdateStep1Progress();
                    break;
                case 2:
                    UpdateStep2Progress();
                    break;
                case 3:
                    UpdateStep3Progress();
                    break;
            }
        }

        /// <summary>
        /// Шаг 1: Безопасная темнота (приглушенный свет дома)
        /// </summary>
        private void UpdateStep1Progress()
        {
            // Проверяем условия
            bool isEvening = Game1.timeOfDay >= 2000 && Game1.timeOfDay <= 2400;
            bool atHome = Game1.player.currentLocation is StardewValley.Locations.FarmHouse;

            if (isEvening && atHome)
            {
                // Применяем бафф "Приглушенный свет"
                if (!_stateService.HasBuffInGame("buffDimLight"))
                {
                    _buffService.ApplyBuff("buffDimLight", "Приглушенный свет", 
                        new StardewValley.Buffs.BuffEffects(), -2);
                    _monitor.Log("[DarknessService] Шаг 1: Бафф 'Приглушенный свет' применен", LogLevel.Debug);
                }

                // Увеличиваем счетчик каждые 6 секунд (10 тиков) = 1 игровая минута
                if (Game1.ticks % 60 == 0) // Каждую секунду реального времени = примерно 1 минута игрового
                {
                    _data.Darkness.SafeDarknessMinutes++;
                    
                    if (_data.Darkness.SafeDarknessMinutes % 5 == 0)
                    {
                        RefreshTherapyQuestJournal(1);
                        Game1.addHUDMessage(new HUDMessage($"Прогресс: {_data.Darkness.SafeDarknessMinutes}/15 минут", HUDMessage.newQuest_type));
                        _monitor.Log($"[DarknessService] Шаг 1: Прогресс {_data.Darkness.SafeDarknessMinutes}/15 минут", LogLevel.Info);
                    }

                    // Проверяем завершение
                    if (_data.Darkness.SafeDarknessMinutes >= 15)
                    {
                        CompleteStep1();
                    }
                }
            }
            else
            {
                // Убираем бафф если покинули дом или не вечер
                if (_stateService.HasBuffInGame("buffDimLight"))
                {
                    _buffService.RemoveBuff("buffDimLight");
                }
            }
        }

        /// <summary>
        /// Завершить Шаг 1 и перейти к Шагу 2
        /// </summary>
        private void CompleteStep1()
        {
            _data.Darkness.CompletedStep1 = true;
            _data.Darkness.TherapyStage = 2;
            
            // Снижаем уровень страха
            if (_data.Darkness.FearLevel > 1)
            {
                _data.Darkness.FearLevel--;
                ApplyFearLevelBuff();
            }

            _buffService.RemoveBuff("buffDimLight");
            
            _monitor.Log("[DarknessService] ✅ Шаг 1 завершен! Переход к Шагу 2", LogLevel.Info);
            
            // Завершаем квест Шага 1
            var quest1 = Game1.player.questLog.FirstOrDefault(q => q.id.Value == "HarveyMod_DarknessStep1");
            if (quest1 != null)
            {
                quest1.questComplete();
                Game1.player.removeQuest("HarveyMod_DarknessStep1");
            }
            
            Game1.playSound("questcomplete");
            Game1.addHUDMessage(new HUDMessage("Шаг 1 завершен! Ты молодец!", HUDMessage.achievement_type));
            
            // Добавляем топик и квест Шага 2
            ConversationHelper.AddTopic("topicDarknessStep1Complete", 2);
            
            // Добавляем квест Шага 2 с задержкой (чтобы игрок сначала увидел сообщение)
            Game1.player.addQuest("HarveyMod_DarknessStep2");
            RefreshTherapyQuestJournal(2);
            _monitor.Log("[DarknessService] Добавлен квест Шага 2", LogLevel.Info);
        }

        /// <summary>
        /// Шаг 2: Контролируемая прогулка (посетить безопасные зоны)
        /// </summary>
        private void UpdateStep2Progress()
        {
            // Прогресс обрабатывается в HandleLocationVisit()
            // Здесь просто проверяем периодические сообщения поддержки
            
            bool isEarlyNight = Game1.timeOfDay >= 2000 && Game1.timeOfDay <= 2200;
            bool isOutside = Game1.player.currentLocation?.IsOutdoors == true;
            
            if (isEarlyNight && isOutside)
            {
                // Периодические сообщения поддержки (5% шанс каждую секунду)
                if (Game1.random.NextDouble() < 0.05)
                {
                    var messages = new[]
                    {
                        "Ты вспоминаешь слова Харви: 'Ты справишься'",
                        "Харви верит в тебя. Ты не одна.",
                        "Каждый шаг делает тебя смелее."
                    };
                    Game1.addHUDMessage(new HUDMessage(messages[Game1.random.Next(messages.Length)], HUDMessage.newQuest_type));
                }
            }
        }

        /// <summary>
        /// Обработать посещение локации для Шага 2
        /// </summary>
        public void HandleLocationVisit(string locationName)
        {
            if (!_data.Darkness.IsTherapyActive || _data.Darkness.TherapyStage != 2) return;

            // Проверяем время (20:00-22:00)
            if (Game1.timeOfDay < 2000 || Game1.timeOfDay > 2200) return;

            // Список безопасных зон
            var safeZones = new[] { "BusStop", "Town" };
            
            if (safeZones.Contains(locationName) && !_data.Darkness.SafeZonesVisited.Contains(locationName))
            {
                _data.Darkness.SafeZonesVisited.Add(locationName);
                
                string zoneName = locationName == "BusStop" ? "Автобусная остановка" : "Город";
                Game1.addHUDMessage(new HUDMessage($"✓ Зона посещена: {zoneName} ({_data.Darkness.SafeZonesVisited.Count}/2)", HUDMessage.achievement_type));
                Game1.playSound("coin");
                
                _monitor.Log($"[DarknessService] Шаг 2: Посещена зона {locationName} ({_data.Darkness.SafeZonesVisited.Count}/2)", LogLevel.Info);
                RefreshTherapyQuestJournal(2);

                // Проверяем завершение
                if (_data.Darkness.SafeZonesVisited.Count >= 2)
                {
                    CompleteStep2();
                }
            }
        }

        /// <summary>
        /// Завершить Шаг 2 и перейти к Шагу 3
        /// </summary>
        private void CompleteStep2()
        {
            _data.Darkness.CompletedStep2 = true;
            _data.Darkness.TherapyStage = 3;
            _data.Darkness.HasReceivedLantern = true; // "Получен" виртуальный фонарь
            
            // Снижаем уровень страха еще больше
            if (_data.Darkness.FearLevel > 0)
            {
                _data.Darkness.FearLevel = 1; // Финальный уровень перед излечением
                ApplyFearLevelBuff();
            }

            _monitor.Log("[DarknessService] ✅ Шаг 2 завершен! Переход к Шагу 3 (финал)", LogLevel.Info);
            
            // Завершаем квест Шага 2
            var quest2 = Game1.player.questLog.FirstOrDefault(q => q.id.Value == "HarveyMod_DarknessStep2");
            if (quest2 != null)
            {
                quest2.questComplete();
                Game1.player.removeQuest("HarveyMod_DarknessStep2");
            }
            
            Game1.playSound("questcomplete");
            Game1.addHUDMessage(new HUDMessage("Шаг 2 завершен! Финальное испытание ждёт.", HUDMessage.achievement_type));
            
            ConversationHelper.AddTopic("topicDarknessStep2Complete", 2);
            ConversationHelper.AddTopic("topicDarknessLanternReceived", 0);
            
            // Добавляем квест Шага 3
            Game1.player.addQuest("HarveyMod_DarknessStep3");
            RefreshTherapyQuestJournal(3);
            _monitor.Log("[DarknessService] Добавлен квест Шага 3 (финал)", LogLevel.Info);
        }

        /// <summary>
        /// Шаг 3: Ночь в горах (финальное испытание)
        /// </summary>
        private void UpdateStep3Progress()
        {
            // Проверяем условия
            bool isLateNight = Game1.timeOfDay >= 2200 && Game1.timeOfDay <= 2600;
            bool inMountains = Game1.player.currentLocation?.NameOrUniqueName == "Mountain";

            if (isLateNight && inMountains)
            {
                // Применяем бафф "Свет надежды" (виртуальный фонарь)
                if (!_stateService.HasBuffInGame("buffHarveyLantern"))
                {
                    var effects = new StardewValley.Buffs.BuffEffects();
                    effects.Defense.Add(2);
                    _buffService.ApplyBuff("buffHarveyLantern", "Свет надежды", effects, -2);
                    Game1.addHUDMessage(new HUDMessage("Фонарь Харви освещает путь...", HUDMessage.newQuest_type));
                    _monitor.Log("[DarknessService] Шаг 3: Бафф 'Свет надежды' применен", LogLevel.Debug);
                }

                // Увеличиваем счетчик каждую секунду
                if (Game1.ticks % 60 == 0)
                {
                    _data.Darkness.MountainNightSeconds++;
                    
                    // Периодические сообщения
                    if (_data.Darkness.MountainNightSeconds % 30 == 0)
                    {
                        var messages = new[]
                        {
                            "Сердце колотится... Но ты справишься.",
                            "Фонарь Харви напоминает: он верит в тебя.",
                            "Каждая секунда — это победа над страхом."
                        };
                        Game1.addHUDMessage(new HUDMessage(messages[Game1.random.Next(messages.Length)], HUDMessage.newQuest_type));
                    }

                    if (_data.Darkness.MountainNightSeconds % 20 == 0)
                    {
                        RefreshTherapyQuestJournal(3);
                        Game1.addHUDMessage(new HUDMessage($"Прогресс: {_data.Darkness.MountainNightSeconds}/120 сек", HUDMessage.newQuest_type));
                        _monitor.Log($"[DarknessService] Шаг 3: Прогресс {_data.Darkness.MountainNightSeconds}/120 сек", LogLevel.Info);
                    }

                    // Проверяем завершение
                    if (_data.Darkness.MountainNightSeconds >= 120)
                    {
                        CompleteStep3();
                    }
                }
            }
            else
            {
                // Убираем бафф если покинули горы
                if (_stateService.HasBuffInGame("buffHarveyLantern"))
                {
                    _buffService.RemoveBuff("buffHarveyLantern");
                }
            }
        }

        /// <summary>
        /// Завершить Шаг 3 - ПОЛНОЕ ИЗЛЕЧЕНИЕ!
        /// </summary>
        private void CompleteStep3()
        {
            _data.Darkness.CompletedStep3 = true;
            
            // Завершаем квест Шага 3
            var quest3 = Game1.player.questLog.FirstOrDefault(q => q.id.Value == "HarveyMod_DarknessStep3");
            if (quest3 != null)
            {
                quest3.questComplete();
                Game1.player.removeQuest("HarveyMod_DarknessStep3");
            }
            
            // Снимаем бафф фонаря
            _buffService.RemoveBuff("buffHarveyLantern");
            
            // Завершаем терапию полностью
            CompleteTherapy();
            
            // Добавляем топик полного излечения
            ConversationHelper.AddTopic("topicDarknessFullyCured", 3);
        }

        // ===== Вспомогательные методы =====

        private string GetCurrentBuffId()
        {
            return _data.Darkness.FearLevel switch
            {
                1 => "buffDarknessLevel1",
                2 => "buffDarknessLevel2",
                3 => "buffDarknessLevel3",
                _ => BuffIds.Darkness // fallback
            };
        }

        private string GetFearLevelDisplayName()
        {
            return _data.Darkness.FearLevel switch
            {
                1 => "Боязнь темноты (легкая)",
                2 => "Боязнь темноты (сильная)",
                3 => "Фобия темноты",
                _ => "Боязнь темноты"
            };
        }

        private StardewValley.Buffs.BuffEffects GetFearLevelEffects(int level)
        {
            var effects = new StardewValley.Buffs.BuffEffects();
            
            switch (level)
            {
                case 1:
                    effects.Defense.Add(-1);
                    break;
                case 2:
                    effects.Defense.Add(-2);
                    break;
                case 3:
                    effects.Defense.Add(-3);
                    break;
            }
            
            return effects;
        }

        private void RemoveOtherFearLevelBuffs(string currentBuffId)
        {
            var allBuffIds = new[] { "buffDarknessLevel1", "buffDarknessLevel2", "buffDarknessLevel3", BuffIds.Darkness };
            
            foreach (var buffId in allBuffIds)
            {
                if (buffId != currentBuffId && _stateService.HasBuffInGame(buffId))
                {
                    _buffService.RemoveBuff(buffId);
                }
            }
        }

        private void RemoveAllFearBuffs()
        {
            var allBuffIds = new[] { "buffDarknessLevel1", "buffDarknessLevel2", "buffDarknessLevel3", BuffIds.Darkness };
            
            foreach (var buffId in allBuffIds)
            {
                _buffService.RemoveBuff(buffId);
            }
        }

        private void ShowFearLevelMessage()
        {
            switch (_data.Darkness.FearLevel)
            {
                case 1:
                    if (Game1.random.NextDouble() < 0.3) // 30% шанс
                        Game1.addHUDMessage(new HUDMessage("Темнота немного пугает...", HUDMessage.error_type));
                    break;
                    
                case 2:
                    if (Game1.random.NextDouble() < 0.5) // 50% шанс
                    {
                        var messages = new[]
                        {
                            "Тени кажутся слишком длинными...",
                            "Сердце бьется чаще в темноте...",
                            "Каждый шорох заставляет вздрагивать..."
                        };
                        Game1.addHUDMessage(new HUDMessage(messages[Game1.random.Next(messages.Length)], HUDMessage.error_type));
                    }
                    break;
                    
                case 3:
                    if (Game1.random.NextDouble() < 0.7) // 70% шанс
                    {
                        var messages = new[]
                        {
                            "Паника нарастает... Нужно вернуться домой!",
                            "Темнота поглощает всё вокруг...",
                            "Сердце колотится... Страх парализует..."
                        };
                        Game1.addHUDMessage(new HUDMessage(messages[Game1.random.Next(messages.Length)], HUDMessage.error_type));
                    }
                    break;
            }
        }

        private void SendHarveyWorryLetter()
        {
            if (_worryLetterSent || ConversationHelper.HasTopic("topicHarveyDarknessWorryLetterSent"))
                return;

            _questService.AddMailForTomorrow(MailIds.DarknessWorry);
            ConversationHelper.AddTopic("topicHarveyDarknessWorryLetterSent", 14);
            _worryLetterSent = true;
            _monitor.Log("[DarknessService] Письмо от Харви о беспокойстве (фobия) запланировано на завтра", LogLevel.Info);
        }

        public void RefreshTherapyQuestJournal(int stage)
        {
            var objective = stage switch
            {
                1 => BuildStep1ObjectiveForInstance(),
                2 => BuildStep2ObjectiveForInstance(),
                3 => BuildStep3ObjectiveForInstance(),
                _ => null,
            };

            if (objective == null)
                return;

            var questId = stage switch
            {
                1 => "HarveyMod_DarknessStep1",
                2 => "HarveyMod_DarknessStep2",
                3 => "HarveyMod_DarknessStep3",
                _ => "",
            };

            if (!string.IsNullOrEmpty(questId))
                _questService.UpdateQuest(questId, objective: objective);
        }

        private string BuildStep1ObjectiveForInstance()
            => $"Дома при приглушенном свете: {_data.Darkness.SafeDarknessMinutes}/15 мин (вечер 20:00–00:00)";

        private string BuildStep2ObjectiveForInstance()
        {
            var visited = _data.Darkness.SafeZonesVisited;
            var bus = visited.Contains("BusStop") ? "✅" : "⬜";
            var town = visited.Contains("Town") ? "✅" : "⬜";
            return $"Безопасные зоны (20:00–22:00): {bus} Автобусная остановка, {town} Город ({visited.Count}/2)";
        }

        private string BuildStep3ObjectiveForInstance()
            => $"Ночь в горах с фонарём Харви: {_data.Darkness.MountainNightSeconds}/120 сек (22:00–02:00)";

        private void ApplyOvercomeBonus()
        {
            // Применяем перманентный бафф "Преодоление": +1 Defense ночью
            var effects = new StardewValley.Buffs.BuffEffects();
            effects.Defense.Add(1);
            _buffService.ApplyBuff("buffDarknessOvercome", "Преодоление", effects, -2);
            
            _monitor.Log("[DarknessService] ✅ Применен перманентный бонус 'Преодоление' (Defense +1 ночью)", LogLevel.Info);
        }

        /// <summary>
        /// ⭐ НОВОЕ: Восстановить бафф страха темноты если он был активен
        /// Вызывается при старте дня для восстановления потерянных баффов
        /// </summary>
        public void RestoreFearBuff()
        {
            // Если игрок полностью излечен, не восстанавливаем бафф
            if (_data.Darkness.IsCured && _data.Darkness.HasOvercomeBonus)
            {
                _monitor.Log("[DarknessService] Игрок излечен от страха темноты, бафф не восстанавливается", LogLevel.Debug);
                return;
            }

            // Если уровень страха > 0 и нет активного баффа
            if (_data.Darkness.FearLevel > 0)
            {
                string currentBuffId = GetCurrentBuffId();
                if (!_stateService.HasBuffInGame(currentBuffId))
                {
                    string displayName = GetFearLevelDisplayName();
                    var effects = GetFearLevelEffects(_data.Darkness.FearLevel);
                    
                    // Удаляем старые баффы других уровней
                    RemoveOtherFearLevelBuffs(currentBuffId);
                    
                    // Восстанавливаем бафф
                    _buffService.ApplyBuff(currentBuffId, displayName, effects, Buff.ENDLESS);
                    _monitor.Log($"[DarknessService] 🔄 Восстановлен бафф {currentBuffId} (уровень {_data.Darkness.FearLevel})", LogLevel.Info);
                    
                    // Восстанавливаем топик если нет активной терапии
                    if (!_data.Darkness.IsTherapyActive && !ConversationHelper.HasTopic(TopicIds.StressDarkness))
                    {
                        ConversationHelper.AddTopic(TopicIds.StressDarkness, 7);
                        _monitor.Log($"[DarknessService] 🔄 Восстановлен топик {TopicIds.StressDarkness}", LogLevel.Debug);
                    }
                }
            }
        }
    }
}

