using System;
using System.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Services;
using HarveyStressMeter.Models;
using HarveyStressMeter.Handlers;
using HarveyStressMeter.Helpers;

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
        private readonly ModResetService _modResetService;
        private readonly StressDialogueService _stressDialogueService;

        public ConsoleCommandHandler(
            IMonitor monitor,
            IModHelper helper,
            SaveData data,
            TreatmentService treatmentService,
            TriggerService triggerService,
            StateService stateService,
            UIHandler uiHandler,
            ModResetService modResetService,
            StressDialogueService stressDialogueService)
        {
            _monitor = monitor;
            _helper = helper;
            _data = data;
            _treatmentService = treatmentService;
            _triggerService = triggerService;
            _stateService = stateService;
            _uiHandler = uiHandler;
            _modResetService = modResetService;
            _stressDialogueService = stressDialogueService;
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
            RegisterResetCommands();
            RegisterTriggerCommands();
        }

        private void RegisterSyncCommands()
        {
            _helper.ConsoleCommands.Add("hs.sync", "Синхронизировать квесты и дебаффы стресса (восстановить пропавшие, очистить завершённые).", (_, __) =>
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

        }

        private void RegisterResetCommands()
        {
            _helper.ConsoleCommands.Add("hs.reset", "Полный сброс мода: топики, дебаффы, квесты и save-данные.", (_, __) => RunFullReset());

            _helper.ConsoleCommands.Add("hs.emergency-reset", "Legacy: используйте 'hs.reset'. Полный сброс мода.", (_, __) =>
            {
                _monitor.Log("⚠️ Команда 'hs.emergency-reset' устарела. Используйте 'hs.reset'", LogLevel.Warn);
                RunFullReset();
            });
        }

        private void RunFullReset()
        {
            _monitor.Log("🔄 Полный сброс HarveyStressMeter...", LogLevel.Warn);

            var result = _modResetService.ResetAll();

            _monitor.Log($"  ✓ Баффов удалено: {result.RemovedBuffs}", LogLevel.Info);
            _monitor.Log($"  ✓ Квестов удалено: {result.RemovedQuests}", LogLevel.Info);
            _monitor.Log($"  ✓ Топиков удалено: {result.RemovedTopics}", LogLevel.Info);
            _monitor.Log("  ✓ Mod save сброшен", LogLevel.Info);
            _monitor.Log("✅ Сброс завершён. Мод в начальном состоянии.", LogLevel.Info);

            Game1.addHUDMessage(new HUDMessage("HarveyStressMeter: полный сброс выполнен", HUDMessage.newQuest_type));
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
            _helper.ConsoleCommands.Add("hs.debug", "hs.debug v2: mod state vs real game (buffs, journal, topics, problems).", (_, __) => ShowFullDiagnostic());
            _helper.ConsoleCommands.Add(
                "stress_dialogue_state",
                "Read-only snapshot: stress dialogue pipeline, debuffs, topics, context.",
                (_, __) => ShowStressDialogueState());
            _helper.ConsoleCommands.Add("hs.clear", "Legacy: используйте 'hs.reset'. Полный сброс мода.", (_, __) =>
            {
                _monitor.Log("⚠️ Команда 'hs.clear' устарела. Используйте 'hs.reset'", LogLevel.Warn);
                RunFullReset();
            });
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
            _monitor.Log($"Social quest in mod state: {_stateService.HasActiveQuestState(QuestIds.Social)}", LogLevel.Info);
            _monitor.Log($"Social quest in real journal: {_stateService.HasQuestInGameJournal(QuestIds.Social)}", LogLevel.Info);
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
            new HsDebugReporter(_data, _stateService, _monitor).WriteFullReport();
        }

        private void ShowStressDialogueState()
        {
            new StressDialogueStateReporter(_data, _stateService, _stressDialogueService, _monitor).WriteReport();
        }
    }
}
