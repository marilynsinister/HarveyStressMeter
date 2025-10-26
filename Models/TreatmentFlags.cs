using System;
using System.Collections.Generic;

namespace HarveyStressMeter.Models
{
    /// <summary>
    /// Служебные флаги для отслеживания состояния лечения
    /// Позволяют независимо от игрового журнала определять активные процессы лечения
    /// </summary>
    public sealed class TreatmentFlags
    {
        /// <summary>
        /// Флаги активного лечения по типам дебаффов
        /// BuffId -> true если лечение активно
        /// </summary>
        public Dictionary<string, bool> ActiveTreatmentFlags { get; set; } = new();

        /// <summary>
        /// Флаги добавления квестов в журнал
        /// QuestId -> true если квест добавлен в журнал
        /// </summary>
        public Dictionary<string, bool> QuestAddedToJournalFlags { get; set; } = new();

        /// <summary>
        /// Временные метки последнего обновления прогресса
        /// BuffId -> DateTime последнего обновления
        /// </summary>
        public Dictionary<string, DateTime> LastProgressUpdate { get; set; } = new();

        /// <summary>
        /// Счетчики для оптимизации (избегаем частых проверок)
        /// </summary>
        public Dictionary<string, int> UpdateSkipCounters { get; set; } = new();

        /// <summary>
        /// Проверяет, активно ли лечение для данного дебаффа
        /// </summary>
        public bool IsTreatmentActive(string buffId)
        {
            return ActiveTreatmentFlags.TryGetValue(buffId, out var isActive) && isActive;
        }

        /// <summary>
        /// Устанавливает флаг активного лечения
        /// </summary>
        public void SetTreatmentActive(string buffId, bool isActive)
        {
            ActiveTreatmentFlags[buffId] = isActive;
            if (isActive)
            {
                LastProgressUpdate[buffId] = DateTime.Now;
                UpdateSkipCounters[buffId] = 0;
            }
            else
            {
                ActiveTreatmentFlags.Remove(buffId);
                LastProgressUpdate.Remove(buffId);
                UpdateSkipCounters.Remove(buffId);
            }
        }

        /// <summary>
        /// Проверяет, добавлен ли квест в журнал
        /// </summary>
        public bool IsQuestAddedToJournal(string questId)
        {
            return QuestAddedToJournalFlags.TryGetValue(questId, out var isAdded) && isAdded;
        }

        /// <summary>
        /// Устанавливает флаг добавления квеста в журнал
        /// </summary>
        public void SetQuestAddedToJournal(string questId, bool isAdded)
        {
            QuestAddedToJournalFlags[questId] = isAdded;
        }

        /// <summary>
        /// Проверяет, нужно ли обновлять прогресс (с учетом интервалов)
        /// </summary>
        public bool ShouldUpdateProgress(string buffId, int skipInterval = 5)
        {
            if (!IsTreatmentActive(buffId))
                return false;

            if (!UpdateSkipCounters.TryGetValue(buffId, out var counter))
            {
                UpdateSkipCounters[buffId] = 0;
                return true;
            }

            UpdateSkipCounters[buffId] = counter + 1;
            return counter % skipInterval == 0;
        }

        /// <summary>
        /// Получает все активные лечения
        /// </summary>
        public IEnumerable<string> GetActiveTreatments()
        {
            return ActiveTreatmentFlags.Where(kvp => kvp.Value).Select(kvp => kvp.Key);
        }

        /// <summary>
        /// Очищает все флаги (при сбросе состояния)
        /// </summary>
        public void ClearAll()
        {
            ActiveTreatmentFlags.Clear();
            QuestAddedToJournalFlags.Clear();
            LastProgressUpdate.Clear();
            UpdateSkipCounters.Clear();
        }
    }
}
