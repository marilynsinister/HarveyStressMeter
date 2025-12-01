using System;
using System.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;
using HarveyStressMeter.Services;
using HarveyStressMeter.Models;
using HarveyStressMeter.Handlers;

namespace HarveyStressMeter.Handlers
{
    /// <summary>
    /// Handles all console commands for the mod
    /// Follows Single Responsibility Principle - only console commands
    /// </summary>
    public class ConsoleCommandHandler
    {
        private readonly IMonitor _monitor;
        private readonly IModHelper _helper;
        private readonly SaveData _data;
        private readonly TreatmentService _treatmentService;
        private readonly TriggerService _triggerService;
        private readonly StateService _stateService;
        private readonly UIHandler _uiHandler;

        public ConsoleCommandHandler(
            IMonitor monitor,
            IModHelper helper,
            SaveData data,
            TreatmentService treatmentService,
            TriggerService triggerService,
            StateService stateService,
            UIHandler uiHandler)
        {
            _monitor = monitor;
            _helper = helper;
            _data = data;
            _treatmentService = treatmentService;
            _triggerService = triggerService;
            _stateService = stateService;
            _uiHandler = uiHandler;
        }

        public void RegisterCommands()
        {
            RegisterBasicCommands();
            RegisterDebugCommands();
        }

        private void RegisterBasicCommands()
        {
            _helper.ConsoleCommands.Add("hs.handbook", "Открыть справочник Харви", (_, __) => _uiHandler.OpenHandbook());

            RegisterSyncCommands();
            RegisterRestoreCommands();
            RegisterCleanupCommands();
            RegisterTriggerCommands();
        }

        private void RegisterSyncCommands()
        {
            _helper.ConsoleCommands.Add("hs.sync", "Синхронизировать квесты и дебаффы стресса (удалить несоответствия).", (_, __) =>
            {
                int before = _data.StressState.ActiveTreatments.Count;
                _treatmentService.SyncQuestsAndBuffs();
                int after = _data.StressState.ActiveTreatments.Count;
                _monitor.Log($"✅ Синхронизация завершена. Лечений: {before} → {after}", LogLevel.Info);
            });

            _helper.ConsoleCommands.Add("harvey_fix_sync", "Legacy command: use 'hs.sync' instead. Resync quests and stress buffs.", (_, __) =>
            {
                _monitor.Log("⚠️ Команда 'harvey_fix_sync' устарела. Используйте 'hs.sync'", LogLevel.Warn);
                int before = _data.StressState.ActiveTreatments.Count;
                _treatmentService.SyncQuestsAndBuffs();
                int after = _data.StressState.ActiveTreatments.Count;
                _monitor.Log($"✅ Синхронизация завершена. Лечений: {before} → {after}", LogLevel.Info);
            });
        }

        private void RegisterRestoreCommands()
        {
            _helper.ConsoleCommands.Add("hs.restore", "Восстановить активные дебаффы стресса и квесты из истории.", (_, __) =>
            {
                _treatmentService.RestoreActiveStressBuffs();
                _monitor.Log("✅ Восстановление активных дебаффов завершено.", LogLevel.Info);
            });

            _helper.ConsoleCommands.Add("hs.restore-from-topics", "Восстановить потерянные квесты из активных топиков стресса.", (_, __) =>
            {
                _treatmentService.RestoreLostQuestsFromTopics();
                _monitor.Log("✅ Восстановление квестов из топиков завершено.", LogLevel.Info);
            });

            // ⭐ НОВОЕ: Команда для ручного восстановления баффов активных лечений
            _helper.ConsoleCommands.Add("hs.restore-buffs", "Восстановить потерянные дебаффы для активных лечений (если баффы исчезли).", (_, __) =>
            {
                int restoredCount = _treatmentService.RestoreMissingBuffsForActiveTreatments();
                _monitor.Log($"✅ Восстановлено {restoredCount} потерянных баффов.", LogLevel.Info);
            });

            _helper.ConsoleCommands.Add("hs.restore-all", "Выполнить полное восстановление: дебаффы, квесты из истории и из топиков.", (_, __) =>
            {
                _monitor.Log("🔄 Начало полного восстановления...", LogLevel.Info);

                _treatmentService.RestoreActiveStressBuffs();
                _monitor.Log("  ✓ Дебаффы и квесты восстановлены из истории", LogLevel.Info);

                _treatmentService.RestoreLostQuestsFromTopics();
                _monitor.Log("  ✓ Квесты восстановлены из топиков", LogLevel.Info);

                _monitor.Log("✅ Полное восстановление завершено.", LogLevel.Info);
            });
        }

        private void RegisterCleanupCommands()
        {
            _helper.ConsoleCommands.Add("hs.cleanup-topics", "Очистить сиротские топики начала лечения (без соответствующих дебаффов).", (_, __) =>
            {
                _treatmentService.CleanupOrphanedTreatmentTopics();
                _monitor.Log("✅ Очистка сиротских топиков завершена.", LogLevel.Info);
            });

            _helper.ConsoleCommands.Add("hs.cleanup-old-topics", "Очистить старые топики стресса (старше 3 дней).", (_, __) =>
            {
                _treatmentService.CleanupOldStressTopics();
                _monitor.Log("✅ Очистка старых топиков завершена.", LogLevel.Info);
            });

            _helper.ConsoleCommands.Add("hs.cleanup-all", "Выполнить все очистки: сиротские топики, старые топики, синхронизация.", (_, __) =>
            {
                _monitor.Log("🧹 Начало комплексной очистки...", LogLevel.Info);
                int before = _data.StressState.ActiveTreatments.Count;

                _treatmentService.CleanupOrphanedTreatmentTopics();
                _monitor.Log("  ✓ Сиротские топики очищены", LogLevel.Info);

                _treatmentService.CleanupOldStressTopics();
                _monitor.Log("  ✓ Старые топики очищены", LogLevel.Info);

                _treatmentService.SyncQuestsAndBuffs();
                int after = _data.StressState.ActiveTreatments.Count;
                _monitor.Log($"  ✓ Синхронизация завершена (лечений: {before} → {after})", LogLevel.Info);

                _monitor.Log("✅ Комплексная очистка завершена.", LogLevel.Info);
            });

            _helper.ConsoleCommands.Add("hs.emergency-reset", "🚨 АВАРИЙНЫЙ СБРОС: Удалить ВСЕ баффы, топики, лечения и квесты стресса.", (_, __) =>
            {
                _monitor.Log("🚨 ================================", LogLevel.Warn);
                _monitor.Log("🚨 АВАРИЙНЫЙ СБРОС НАЧАТ", LogLevel.Warn);
                _monitor.Log("🚨 ================================", LogLevel.Warn);

                // 1. Удаляем все стрессовые баффы
                var stressBuffIds = new[] { 
                    "buffStressSocial",
                    "buffStressTired", "buffStressThunder", "buffStressTooCold", 
                    "buffStressOverwork", "buffStressDarkness", "buffDarknessLevel1", "buffDarknessLevel2", "buffDarknessLevel3",
                    "buffStressLonely", "buffStressHunger", "buffStressNoSleep",
                    "buffImmunity", "buffCareAura", "buffRestingAtHome", "buffDimLight", 
                    "buffOverworkBreak", "buffThunderCalming", "buffLightAndSafe"
                };
                
                int removedBuffs = 0;
                foreach (var buffId in stressBuffIds)
                {
                    if (Game1.player.hasBuff(buffId))
                    {
                        Game1.player.buffs.Remove(buffId);
                        removedBuffs++;
                    }
                }
                _monitor.Log($"  ✓ Удалено баффов: {removedBuffs}", LogLevel.Info);

                // 2. Удаляем все квесты стресса
                var questIds = new[] {
                    "HarveyMod_SocialRecovery", "HarveyMod_RestAndRecovery", "HarveyMod_ThunderTherapy",
                    "HarveyMod_WarmthTherapy", "HarveyMod_OverworkBreaks", "HarveyMod_DarknessStep1",
                    "HarveyMod_DarknessStep2", "HarveyMod_NoSleepTherapy", "HarveyMod_LonelyTherapy",
                    "HarveyMod_HungerTherapy"
                };

                int removedQuests = 0;
                foreach (var questId in questIds)
                {
                    var quest = Game1.player.questLog.FirstOrDefault(q => q.id.Value == questId);
                    if (quest != null)
                    {
                        Game1.player.questLog.Remove(quest);
                        removedQuests++;
                    }
                }
                _monitor.Log($"  ✓ Удалено квестов: {removedQuests}", LogLevel.Info);

                // 3. Очищаем все топики стресса
                var topicPrefixes = new[] { "topic", "Stress", "Treatment", "Harvey" };
                int removedTopics = 0;
                var topicsToRemove = Game1.player.activeDialogueEvents.Keys
                    .Where(k => topicPrefixes.Any(p => k.Contains(p)))
                    .ToList();
                
                foreach (var topic in topicsToRemove)
                {
                    Game1.player.activeDialogueEvents.Remove(topic);
                    removedTopics++;
                }
                _monitor.Log($"  ✓ Удалено топиков: {removedTopics}", LogLevel.Info);

                // 4. Очищаем состояние мода
                _data.StressState.ActiveTreatments.Clear();
                _data.TalkedNpcsToday.Clear();
                _data.DaysWithoutTalking = 0;
                _data.DaysWithoutEating = 0;
                _data.DaysWithLateSleep = 0;
                _data.OverworkBreaksToday = 0;
                _data.OverworkBreakSeconds = 0;
                _data.OverworkBreakActive = false;
                _data.Darkness = new DarknessProgress();
                _monitor.Log($"  ✓ Состояние мода очищено", LogLevel.Info);

                _monitor.Log("🚨 ================================", LogLevel.Warn);
                _monitor.Log("✅ АВАРИЙНЫЙ СБРОС ЗАВЕРШЕН", LogLevel.Warn);
                _monitor.Log("🚨 ================================", LogLevel.Warn);
                _monitor.Log("ℹ️  Все стрессовые дебаффы, квесты, топики и лечения удалены.", LogLevel.Info);
                _monitor.Log("ℹ️  Игра вернулась в начальное состояние.", LogLevel.Info);
            });
        }

        private void RegisterTriggerCommands()
        {
            _helper.ConsoleCommands.Add("hs.trigger", "Вручную проверить триггеры завершения квестов.", (_, __) =>
            {
                _triggerService.CheckManualTriggers();
                _monitor.Log("✅ Проверка триггеров завершена.", LogLevel.Info);
            });
        }

        private void RegisterDebugCommands()
        {
            _helper.ConsoleCommands.Add("hs.debug-quests", "Debug quest system - check quest data and availability.", (_, __) => DebugQuestSystem());
            _helper.ConsoleCommands.Add("hs.states", "Show all stress buff states.", (_, __) => ShowTreatmentStates());
            _helper.ConsoleCommands.Add("hs.debug", "Show full diagnostic info (topics, buffs, states).", (_, __) => ShowFullDiagnostic());
            _helper.ConsoleCommands.Add("hs.clear", "Clear all stress buffs, quests and topics (emergency reset).", (_, __) => ClearAllStressStates());
        }

        private void DebugQuestSystem()
        {
            _monitor.Log("=== QUEST DEBUG ===", LogLevel.Info);
            var questData = Game1.content.Load<Dictionary<string, string>>("Data/Quests");
            _monitor.Log($"Data/Quests.Count: {questData.Count}", LogLevel.Info);

            var harveyQuests = questData.Keys.Where(k => k.Contains("Harvey")).ToList();
            _monitor.Log($"Harvey quests in Data/Quests: {harveyQuests.Count}", LogLevel.Info);
            foreach (var quest in harveyQuests.Take(10))
            {
                _monitor.Log($"  • {quest}", LogLevel.Info);
            }

            _monitor.Log($"Player quests in journal: {Game1.player.questLog.Count}", LogLevel.Info);
            for (int i = 0; i < Math.Min(Game1.player.questLog.Count, 5); i++)
            {
                var quest = Game1.player.questLog[i];
                var id = quest.id.Value;
                var questType = quest.GetType().Name;

                if (string.IsNullOrWhiteSpace(id))
                    _monitor.Log($"  • [{i}] Тип: {questType}", LogLevel.Info);
                else
                    _monitor.Log($"  • [{i}] ID: {id}, Тип: {questType}", LogLevel.Info);
            }

            _monitor.Log($"Social quest HarveyMod_SocialRecovery in Data/Quests: {questData.ContainsKey("HarveyMod_SocialRecovery")}", LogLevel.Info);
            _monitor.Log($"Social quest in journal: {_stateService.HasQuestInJournal("HarveyMod_SocialRecovery")}", LogLevel.Info);
        }

        private void ShowTreatmentStates()
        {
            _monitor.Log($"=== TreatmentHistory (всего: {_data.StressState.TreatmentHistory.Count}) ===", LogLevel.Info);
            foreach (var (buffId, historyList) in _data.StressState.TreatmentHistory)
            {
                if (historyList.Count > 0)
                {
                    var treatment = historyList.Last();
                    var displayName = treatment.TreatmentStarted ? "ЛЕЧЕНИЕ НАЧАТО" : "НЕ НАЧАТО";
                    var curedStatus = treatment.IsCured ? "ВЫЛЕЧЕН" : "НЕ ВЫЛЕЧЕН";
                    var daysSince = SDate.Now().DaysSinceStart - treatment.IssuedDate.DaysSinceStart;
                    _monitor.Log($"  {buffId}: выдан={treatment.IssuedDate} ({daysSince}д назад), лечение={displayName}, статус={curedStatus}", LogLevel.Info);
                }
            }
        }

        private void ShowFullDiagnostic()
        {
            _monitor.Log("=== ДИАГНОСТИКА СИСТЕМЫ СТРЕССА ===", LogLevel.Info);

            ShowActiveTopics();
            ShowActiveStressBuffs();
            ShowActiveTreatments();
            ShowTreatmentHistory();
            ShowConversationState();
            ShowTreatmentProgress();
            ShowDetailedAnalysis();

            _monitor.Log("\n=== КОНЕЦ ДИАГНОСТИКИ ===", LogLevel.Info);
        }

        private void ShowActiveTopics()
        {
            var topics = Game1.player.activeDialogueEvents;
            _monitor.Log($"\n📋 Активные топики:", LogLevel.Info);
            if (topics != null && topics.Count() > 0)
            {
                int foundCount = 0;
                foreach (var key in topics.Keys)
                {
                    if (key.Contains("Stress") || key.Contains("Treatment") || key.Contains("Harvey"))
                    {
                        topics.TryGetValue(key, out int days);
                        _monitor.Log($"  • {key} = {days} дней", LogLevel.Info);
                        foundCount++;
                    }
                }
                if (foundCount == 0)
                {
                    _monitor.Log("  Нет топиков, связанных со стрессом", LogLevel.Info);
                }
            }
            else
            {
                _monitor.Log("  Нет активных топиков", LogLevel.Info);
            }
        }

        private void ShowActiveStressBuffs()
        {
            _monitor.Log($"\n🔴 Активные баффы стресса:", LogLevel.Info);
            var stressBuffIds = new[] { "HarveyMod_Tired", "HarveyMod_Lonely", "HarveyMod_Thunder", "HarveyMod_Hunger",
                "HarveyMod_Overwork", "HarveyMod_NoSleep", "HarveyMod_TooCold", "HarveyMod_Social", "HarveyMod_Darkness" };
            bool hasAnyBuff = false;
            foreach (var buffId in stressBuffIds)
            {
                if (_stateService.HasActiveBuffInGame(buffId))
                {
                    var isLocked = _data.StressState.IsTreatmentLocked(buffId) ? "🔒 ЗАЛОЧЕН" : "⚪ Свободный";
                    _monitor.Log($"  • {buffId} - {isLocked}", LogLevel.Info);
                    hasAnyBuff = true;
                }
            }
            if (!hasAnyBuff) _monitor.Log("  Нет активных баффов стресса", LogLevel.Info);
        }

        private void ShowActiveTreatments()
        {
            _monitor.Log($"\n🔒 Активные лечения (всего: {_data.StressState.GetActiveTreatmentsCount()}):", LogLevel.Info);
            if (_data.StressState.ActiveTreatments.Count > 0)
            {
                foreach (var kvp in _data.StressState.ActiveTreatments)
                {
                    var questId = kvp.Value.QuestId ?? "нет квеста";
                    _monitor.Log($"  • {kvp.Key} → квест: {questId}", LogLevel.Info);
                }
            }
            else
            {
                _monitor.Log("  Нет залоченных дебаффов", LogLevel.Info);
            }
        }

        private void ShowTreatmentHistory()
        {
            _monitor.Log($"\n📊 История лечений (всего: {_data.StressState.TreatmentHistory.Count}):", LogLevel.Info);
            if (_data.StressState.TreatmentHistory.Count > 0)
            {
                foreach (var (buffId, historyList) in _data.StressState.TreatmentHistory)
                {
                    if (historyList.Count > 0)
                    {
                        var treatment = historyList.Last();
                        var daysSince = SDate.Now().DaysSinceStart - treatment.IssuedDate.DaysSinceStart;
                        var status = treatment.IsCured ? "✅ Вылечен" :
                                    (treatment.TreatmentStarted ? "🔄 Лечение начато" : "⏳ Ожидание");
                        _monitor.Log($"  • {buffId}: {status}, выдан {treatment.IssuedDate} ({daysSince}д назад)", LogLevel.Info);
                    }
                }
            }
            else
            {
                _monitor.Log("  Нет сохраненных состояний", LogLevel.Info);
            }
        }

        private void ShowConversationState()
        {
            _monitor.Log($"\n💬 Состояние разговоров:", LogLevel.Info);
            _monitor.Log($"  • Разговоров сегодня: {_data.TalkedNpcsToday.Count}", LogLevel.Info);
            if (_data.TalkedNpcsToday.Count > 0)
            {
                foreach (var npc in _data.TalkedNpcsToday)
                {
                    _monitor.Log($"    - {npc}", LogLevel.Info);
                }
            }
        }

        private void ShowTreatmentProgress()
        {
            _monitor.Log($"\n🏥 Прогресс лечения:", LogLevel.Info);
            bool hasActiveTreatment = false;
            foreach (var (buffId, treatment) in _data.StressState.ActiveTreatments)
            {
                if (treatment.TreatmentStarted && !string.IsNullOrEmpty(treatment.QuestId))
                {
                    hasActiveTreatment = true;
                    var progress = treatment.Progress;
                    if (progress != null)
                    {
                        _monitor.Log($"  • {buffId}: разговоры={progress.TalkedUniqueToday}, время с Харви={progress.SecondsNearHarvey}с", LogLevel.Info);
                    }
                }
            }
            if (!hasActiveTreatment)
            {
                _monitor.Log("  Нет активного лечения", LogLevel.Info);
            }
        }

        private void ShowDetailedAnalysis()
        {
            _monitor.Log("\n🔍 ДЕТАЛЬНЫЙ АНАЛИЗ СОСТОЯНИЯ:", LogLevel.Info);
            _monitor.Log($"ActiveTreatments: {_data.StressState.ActiveTreatments.Count}", LogLevel.Info);
            _monitor.Log($"TreatmentHistory: {_data.StressState.TreatmentHistory.Count}", LogLevel.Info);
            _monitor.Log($"TalkedNpcsToday: {_data.TalkedNpcsToday.Count}", LogLevel.Info);
        }

        private void ClearAllStressStates()
        {
            int removedBuffs = 0;
            int removedQuests = 0;
            int removedTopics = 0;

            var allBuffIds = new[]
            {
                "HarveyMod_Tired", "HarveyMod_Lonely", "HarveyMod_Thunder", "HarveyMod_Hunger",
                "HarveyMod_Overwork", "HarveyMod_NoSleep", "HarveyMod_TooCold", "HarveyMod_Social", "HarveyMod_Darkness"
            };

            foreach (var buffId in allBuffIds)
            {
                if (_stateService.HasActiveBuffInGame(buffId))
                {
                    // Note: BuffService.RemoveBuff is not accessible here, using direct buff removal
                    Game1.player.buffs.Remove(buffId);
                    removedBuffs++;
                }
            }

            var allQuestIds = new[]
            {
                "HarveyMod_Tired", "HarveyMod_Lonely", "HarveyMod_Thunder", "HarveyMod_Hunger",
                "HarveyMod_Overwork", "HarveyMod_NoSleep", "HarveyMod_TooCold", "HarveyMod_Social", "HarveyMod_Darkness"
            };

            foreach (var questId in allQuestIds)
            {
                if (_stateService.HasQuestInJournal(questId))
                {
                    _stateService.CompleteTreatment(questId);
                    removedQuests++;
                }
            }

            var allTopics = new[]
            {
                "topicStressTired", "topicStressLonely", "topicStressThunder", "topicStressHunger",
                "topicStressOverwork", "topicStressNoSleep", "topicStressTooCold", "topicStressSocial",
                "topicStressDarkness", "topicLonelyPending", "topicOverworkBreakActive",
                "topicOverworkBreakInterrupted", "topicAteToday", "topicSpokeToday",
                "topicStressTreatmentStartTired", "topicStressTreatmentStartLonely", "topicStressTreatmentStartThunder",
                "topicStressTreatmentStartHunger", "topicStressTreatmentStartOverwork", "topicStressTreatmentStartNoSleep",
                "topicStressTreatmentStartTooCold", "topicStressTreatmentStartSocial", "topicStressTreatmentStartDarkness",
                "topicStressTreatmentStarted"
            };

            foreach (var topic in allTopics)
            {
                if (Game1.player.activeDialogueEvents.ContainsKey(topic))
                {
                    Game1.player.activeDialogueEvents.Remove(topic);
                    removedTopics++;
                }
            }

            _data.StressState.ActiveTreatments.Clear();
            _data.StressState.LastIssuedDay.Clear();
            _data.StressState.TreatmentHistory.Clear();
            _data.TalkedNpcsToday.Clear();
            _data.OverworkBreaksToday = 0;
            _data.OverworkBreakSeconds = 0;
            _data.OverworkBreakActive = false;
            _data.TalkedToHarveyToday = false;

            SaveData();

            _monitor.Log($"Все состояния стресса очищены: {removedBuffs} баффов, {removedQuests} квестов, {removedTopics} топиков.", LogLevel.Info);
            Game1.addHUDMessage(new HUDMessage("Все состояния стресса очищены", HUDMessage.newQuest_type));
        }

        private void SaveData()
        {
            _helper.Data.WriteSaveData("stress-data-v1", _data);
        }
    }
}
