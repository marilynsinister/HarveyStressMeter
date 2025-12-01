using StardewValley;
using StardewModdingAPI;

namespace HarveyStressMeter.Services
{
    /// <summary>
    /// Сервис для управления квестами
    /// </summary>
    public class QuestService
    {
        private readonly IMonitor? _monitor;

        public QuestService(IMonitor? monitor = null)
        {
            _monitor = monitor;
        }

        /// <summary>
        /// Проверяет наличие квеста в журнале игрока
        /// </summary>
        public bool HasQuest(string questId)
        {
            return Game1.player.hasQuest(questId);
        }

        /// <summary>
        /// Обновляет описание и/или цель квеста в журнале
        /// </summary>
        /// <param name="questId">ID квеста</param>
        /// <param name="description">Новое описание квеста (null если не нужно обновлять)</param>
        /// <param name="objective">Новая цель квеста (null если не нужно обновлять)</param>
        public void UpdateQuest(string questId, string? description = null, string? objective = null)
        {
            if (!HasQuest(questId))
            {
                _monitor?.Log($"[QuestService.UpdateQuest] ⚠️ Квест '{questId}' не найден в журнале", LogLevel.Warn);
                return;
            }

            var quest = Game1.player.questLog.FirstOrDefault(q => q.id.Value == questId);
            if (quest == null)
            {
                _monitor?.Log($"[QuestService.UpdateQuest] ⚠️ Квест '{questId}' не найден в questLog", LogLevel.Warn);
                return;
            }

            bool updated = false;

            if (description != null)
            {
                quest.questDescription = description;
                // Логирование убрано для оптимизации (вызывается часто)
                updated = true;
            }

            if (objective != null)
            {
                quest.currentObjective = objective;
                // Логирование убрано для оптимизации (вызывается часто)
                updated = true;
            }

            if (!updated)
            {
                // Логирование убрано - это нормальная ситуация, не ошибка
            }
        }

        /// <summary>
        /// Добавляет квест в журнал игрока
        /// ⭐ ИСПРАВЛЕНО: Добавлена защита от конфликтов с другими модами (HelpWanted и др.)
        /// </summary>
        public void AddQuest(string questId)
        {
            if (HasQuest(questId))
            {
                _monitor?.Log($"[QuestService.AddQuest] ⚠️ Квест '{questId}' уже в журнале", LogLevel.Warn);
                return;
            }

            try
            {
                Game1.player.addQuest(questId);
                _monitor?.Log($"[QuestService.AddQuest] ✅ Квест '{questId}' добавлен", LogLevel.Info);
            }
            catch (NullReferenceException ex)
            {
                _monitor?.Log($"[QuestService.AddQuest] ❌ Ошибка при добавлении квеста '{questId}': NullReferenceException. Возможно конфликт с другим модом (HelpWanted?) или квест не найден в Data/Quests.", LogLevel.Error);
                _monitor?.Log($"[QuestService.AddQuest] Stack trace: {ex.StackTrace}", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                _monitor?.Log($"[QuestService.AddQuest] ❌ Непредвиденная ошибка при добавлении квеста '{questId}': {ex.Message}", LogLevel.Error);
                _monitor?.Log($"[QuestService.AddQuest] Stack trace: {ex.StackTrace}", LogLevel.Debug);
            }
        }

        /// <summary>
        /// Завершает квест
        /// </summary>
        public void CompleteQuest(string questId)
        {
            if (!HasQuest(questId))
            {
                //_monitor?.Log($"[QuestService.CompleteQuest] ⚠️ Квест '{questId}' не найден", LogLevel.Warn);
                return;
            }

            Game1.player.completeQuest(questId);
            _monitor?.Log($"[QuestService.CompleteQuest] ✅ Квест '{questId}' завершен", LogLevel.Info);
        }

        /// <summary>
        /// Добавляет письмо на завтра
        /// </summary>
        public void AddMailForTomorrow(string mailId)
        {
            Game1.addMailForTomorrow(mailId);
            _monitor?.Log($"[QuestService.AddMailForTomorrow] ✅ Письмо '{mailId}' добавлено на завтра", LogLevel.Debug);
        }
    }
}

