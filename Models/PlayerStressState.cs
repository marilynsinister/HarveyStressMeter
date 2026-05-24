using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI.Utilities;

namespace HarveyStressMeter.Models
{
    /// <summary>
    /// Полное состояние системы стресса для игрока
    /// Единый источник правды для всей логики мода
    /// </summary>
    public sealed class PlayerStressState
    {
        /// <summary>
        /// ⭐ УЛУЧШЕНО: Активные лечения (TreatmentKey -> TreatmentState)
        /// Использует уникальные ключи для поддержки множественных лечений одного типа
        /// </summary>
        public Dictionary<string, TreatmentState> ActiveTreatments { get; set; } = new();

        /// <summary>
        /// ⭐ НОВОЕ: Быстрый доступ к активным лечениям по типу баффа
        /// BuffId -> список TreatmentKey для этого типа баффа
        /// </summary>
        public Dictionary<string, List<string>> ActiveTreatmentsByBuff { get; set; } = new();

        /// <summary>
        /// История всех лечений (включая завершенные)
        /// BuffId -> список всех экземпляров этого лечения
        /// </summary>
        public Dictionary<string, List<TreatmentState>> TreatmentHistory { get; set; } = new();

        /// <summary>
        /// Последний день выдачи каждого баффа (для кулдауна)
        /// </summary>
        public Dictionary<string, SDate> LastIssuedDay { get; set; } = new();

        /// <summary>
        /// Служебные переменные для отслеживания состояния лечения
        /// </summary>
        public TreatmentFlags TreatmentFlags { get; set; } = new();

        /// <summary>
        /// ⭐ НОВОЕ: Индивидуальные иммунитеты для каждого дебаффа
        /// BuffId -> дата окончания иммунитета (SDate)
        /// Иммунитет предотвращает повторное применение дебаффа до указанной даты
        /// </summary>
        public Dictionary<string, SDate> DebuffImmunities { get; set; } = new();

        /// <summary>
        /// ⭐ УЛУЧШЕНО: Проверяет, есть ли активный дебафф (поддерживает множественные лечения)
        /// </summary>
        public bool HasActiveBuff(string buffId)
        {
            return GetActiveTreatmentsByBuff(buffId).Any(t => !t.IsCured);
        }

        /// <summary>
        /// ⭐ НОВОЕ: Получает все активные лечения определенного типа баффа
        /// </summary>
        public IEnumerable<TreatmentState> GetActiveTreatmentsByBuff(string buffId)
        {
            if (!ActiveTreatmentsByBuff.TryGetValue(buffId, out var treatmentKeys))
                return Enumerable.Empty<TreatmentState>();

            return treatmentKeys
                .Where(key => ActiveTreatments.TryGetValue(key, out var treatment))
                .Select(key => ActiveTreatments[key])
                .Where(t => !t.IsCured);
        }

        /// <summary>
        /// ⭐ НОВОЕ: Получает количество активных лечений определенного типа
        /// </summary>
        public int GetActiveTreatmentCountByBuff(string buffId)
        {
            return GetActiveTreatmentsByBuff(buffId).Count();
        }

        /// <summary>
        /// Проверяет, есть ли активный квест лечения
        /// </summary>
        public bool HasActiveQuest(string questId)
        {
            return ActiveTreatments.Values.Any(t => t.QuestId == questId && t.IsTreatmentActive());
        }

        /// <summary>
        /// ⭐ УЛУЧШЕНО: Получает первое активное лечение по ID дебаффа (для обратной совместимости)
        /// </summary>
        public TreatmentState? GetActiveTreatment(string buffId)
        {
            return GetActiveTreatmentsByBuff(buffId)
                .OrderByDescending(t => t.TreatmentStarted)
                .ThenByDescending(t => t.IssuedDate.DaysSinceStart)
                .FirstOrDefault();
        }

        /// <summary>
        /// ⭐ НОВОЕ: Получает лечение по уникальному ключу
        /// </summary>
        public TreatmentState? GetTreatmentByKey(string treatmentKey)
        {
            return ActiveTreatments.TryGetValue(treatmentKey, out var treatment) ? treatment : null;
        }

        /// <summary>
        /// Получает активное лечение по ID квеста
        /// </summary>
        public TreatmentState? GetActiveTreatmentByQuest(string questId)
        {
            return ActiveTreatments.Values.FirstOrDefault(t => t.QuestId == questId && t.IsTreatmentActive());
        }

        /// <summary>
        /// Получает количество завершений лечения
        /// </summary>
        public int GetTreatmentCompletionCount(string buffId)
        {
            if (!TreatmentHistory.TryGetValue(buffId, out var history))
                return 0;

            return history.Count(t => t.IsCured);
        }

        /// <summary>
        /// Получает дату последнего завершения лечения
        /// </summary>
        public SDate? GetLastTreatmentCompletionDate(string buffId)
        {
            if (!TreatmentHistory.TryGetValue(buffId, out var history))
                return null;

            var completed = history
                .Where(t => t.IsCured && t.CompletedDate != null)
                .OrderByDescending(t => t.CompletedDate!.DaysSinceStart)
                .FirstOrDefault();

            return completed?.CompletedDate;
        }

        /// <summary>
        /// Добавляет лечение в историю
        /// </summary>
        public void AddTreatmentToHistory(string buffId, TreatmentState treatment)
        {
            if (!TreatmentHistory.ContainsKey(buffId))
                TreatmentHistory[buffId] = new List<TreatmentState>();
            
            TreatmentHistory[buffId].Add(treatment);
        }

        /// <summary>
        /// ⭐ УЛУЧШЕНО: Проверяет, заблокировано ли лечение (есть в ActiveTreatments)
        /// </summary>
        public bool IsTreatmentLocked(string buffId)
        {
            return GetActiveTreatmentCountByBuff(buffId) > 0;
        }

        /// <summary>
        /// ⭐ НОВОЕ: Добавляет лечение с уникальным ключом
        /// </summary>
        public void AddTreatment(TreatmentState treatment)
        {
            // Убеждаемся, что у лечения есть уникальный ключ
            treatment.EnsureTreatmentKey();

            // Добавляем в основную коллекцию
            ActiveTreatments[treatment.TreatmentKey] = treatment;

            // Добавляем в индекс по типу баффа
            if (!ActiveTreatmentsByBuff.ContainsKey(treatment.BuffId))
                ActiveTreatmentsByBuff[treatment.BuffId] = new List<string>();

            if (!ActiveTreatmentsByBuff[treatment.BuffId].Contains(treatment.TreatmentKey))
                ActiveTreatmentsByBuff[treatment.BuffId].Add(treatment.TreatmentKey);

            // Добавляем в историю
            AddTreatmentToHistory(treatment.BuffId, treatment);
        }

        /// <summary>
        /// ⭐ НОВОЕ: Удаляет лечение по уникальному ключу
        /// </summary>
        public bool RemoveTreatment(string treatmentKey)
        {
            if (!ActiveTreatments.TryGetValue(treatmentKey, out var treatment))
                return false;

            // Удаляем из основной коллекции
            ActiveTreatments.Remove(treatmentKey);

            // Удаляем из индекса по типу баффа
            if (ActiveTreatmentsByBuff.TryGetValue(treatment.BuffId, out var keys))
            {
                keys.Remove(treatmentKey);
                if (keys.Count == 0)
                    ActiveTreatmentsByBuff.Remove(treatment.BuffId);
            }

            return true;
        }

        /// <summary>
        /// ⭐ НОВОЕ: Генерирует следующий номер экземпляра для типа баффа
        /// </summary>
        public int GetNextInstanceNumber(string buffId)
        {
            if (!ActiveTreatmentsByBuff.TryGetValue(buffId, out var keys))
                return 1;

            return keys.Count + 1;
        }

        /// <summary>
        /// Получает последнее лечение из истории
        /// </summary>
        public TreatmentState? GetLastTreatmentFromHistory(string buffId)
        {
            if (!TreatmentHistory.TryGetValue(buffId, out var history) || history.Count == 0)
                return null;
            
            return history.Last();
        }

        /// <summary>
        /// Проверяет, есть ли валидное лечение в истории (не вылеченное и не устаревшее)
        /// </summary>
        public bool HasValidTreatmentInHistory(string buffId, SDate currentDate)
        {
            var lastTreatment = GetLastTreatmentFromHistory(buffId);
            return lastTreatment != null && !lastTreatment.IsCured && lastTreatment.ShouldRestore(currentDate);
        }

        /// <summary>
        /// Получает все активные лечения с начатым лечением (есть квест)
        /// </summary>
        public IEnumerable<TreatmentState> GetActiveTreatmentsWithQuests()
        {
            return ActiveTreatments.Values.Where(t => t.IsTreatmentActive() && !string.IsNullOrEmpty(t.QuestId));
        }

        /// <summary>
        /// Получает количество активных лечений
        /// </summary>
        public int GetActiveTreatmentsCount()
        {
            return ActiveTreatments.Count(t => t.Value.IsTreatmentActive());
        }

        /// <summary>
        /// Проверяет, есть ли активный бафф в игре (через BuffService)
        /// </summary>
        public bool HasActiveBuffInGame(string buffId)
        {
            // Этот метод должен вызываться через StateService для доступа к BuffService
            throw new InvalidOperationException("Используйте StateService.HasActiveBuffInGame() для проверки баффов в игре");
        }

        /// <summary>
        /// Проверяет, есть ли квест в журнале игры (через QuestService)
        /// </summary>
        public bool HasQuestInJournal(string questId)
        {
            // Этот метод должен вызываться через StateService для доступа к QuestService
            throw new InvalidOperationException("Используйте StateService.HasQuestInJournal() для проверки квестов в журнале");
        }

        /// <summary>
        /// Проверяет, можно ли выдать новый бафф (учитывая кулдаун)
        /// </summary>
        public bool CanIssueNewBuff(string buffId, int cooldownDays = 7)
        {
            if (!LastIssuedDay.TryGetValue(buffId, out var lastIssued))
                return true; // Никогда не выдавался

            var today = SDate.Now();
            var daysSinceLastIssue = today.DaysSinceStart - lastIssued.DaysSinceStart;
            return daysSinceLastIssue >= cooldownDays;
        }

        /// <summary>
        /// Получает все ID баффов стресса
        /// </summary>
        public static string[] GetAllStressBuffIds()
        {
            return new[]
            {
                "HarveyMod_Tired", "HarveyMod_Lonely", "HarveyMod_Thunder", "HarveyMod_Hunger",
                "HarveyMod_Overwork", "HarveyMod_NoSleep", "HarveyMod_TooCold", "HarveyMod_Social", "HarveyMod_Darkness"
            };
        }

        /// <summary>
        /// Получает все ID квестов лечения
        /// </summary>
        public static string[] GetAllTreatmentQuestIds()
        {
            return new[]
            {
                "HarveyMod_Tired", "HarveyMod_Lonely", "HarveyMod_Thunder", "HarveyMod_Hunger",
                "HarveyMod_Overwork", "HarveyMod_NoSleep", "HarveyMod_TooCold", "HarveyMod_Social", "HarveyMod_Darkness"
            };
        }

        /// <summary>
        /// ⭐ НОВОЕ: Проверяет, есть ли активный иммунитет для дебаффа
        /// </summary>
        public bool HasImmunity(string buffId)
        {
            if (!DebuffImmunities.TryGetValue(buffId, out var immunityEndDate))
                return false;

            var today = SDate.Now();
            // Иммунитет активен, если сегодняшняя дата меньше или равна дате окончания
            return today.DaysSinceStart <= immunityEndDate.DaysSinceStart;
        }

        /// <summary>
        /// ⭐ НОВОЕ: Устанавливает иммунитет для дебаффа на указанное количество дней
        /// </summary>
        public void SetImmunity(string buffId, int days)
        {
            var today = SDate.Now();
            var endDate = today.AddDays(days);
            DebuffImmunities[buffId] = endDate;
        }

        /// <summary>
        /// ⭐ НОВОЕ: Удаляет иммунитет для дебаффа (если есть)
        /// </summary>
        public void RemoveImmunity(string buffId)
        {
            DebuffImmunities.Remove(buffId);
        }

        /// <summary>
        /// ⭐ НОВОЕ: Очищает истекшие иммунитеты
        /// Вызывается каждый день для очистки устаревших записей
        /// </summary>
        public void CleanupExpiredImmunities()
        {
            var today = SDate.Now();
            var expiredKeys = DebuffImmunities
                .Where(kvp => today.DaysSinceStart > kvp.Value.DaysSinceStart)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                DebuffImmunities.Remove(key);
            }
        }

        /// <summary>
        /// ⭐ НОВОЕ: Получает дату окончания иммунитета (если есть)
        /// </summary>
        public SDate? GetImmunityEndDate(string buffId)
        {
            return DebuffImmunities.TryGetValue(buffId, out var endDate) ? endDate : null;
        }
    }
}