using StardewModdingAPI.Utilities;

namespace HarveyStressMeter.Models
{
    /// <summary>
    /// Объединенное состояние лечения - содержит всю информацию о дебаффе и связанном квесте
    /// Заменяет StressBuffState и QuestState для упрощения структуры
    /// </summary>
    public sealed class TreatmentState
    {
        // ===== ОСНОВНАЯ ИНФОРМАЦИЯ =====
        
        /// <summary>
        /// ID дебаффа стресса
        /// </summary>
        public string BuffId { get; set; } = "";

        /// <summary>
        /// ID связанного квеста лечения
        /// </summary>
        public string QuestId { get; set; } = "";

        /// <summary>
        /// ⭐ НОВОЕ: Уникальный ключ для лечения (позволяет множественные лечения одного типа)
        /// Формат: "{BuffId}_{Timestamp}" или "{BuffId}_{InstanceNumber}"
        /// </summary>
        public string TreatmentKey { get; set; } = "";

        /// <summary>
        /// ⭐ НОВОЕ: Номер экземпляра лечения (для множественных лечений одного типа)
        /// </summary>
        public int InstanceNumber { get; set; } = 1;

        // ===== ДАТЫ =====
        
        /// <summary>
        /// Дата выдачи дебаффа
        /// </summary>
        public SDate IssuedDate { get; set; } = SDate.Now();

        /// <summary>
        /// Дата начала лечения (выдачи квеста)
        /// </summary>
        public SDate? TreatmentStartedDate { get; set; } = null;

        /// <summary>
        /// Дата завершения лечения
        /// </summary>
        public SDate? CompletedDate { get; set; } = null;

        // ===== СТАТУСЫ =====
        
        /// <summary>
        /// Был ли начат процесс лечения (выдан квест)
        /// </summary>
        public bool TreatmentStarted { get; set; } = false;

        /// <summary>
        /// Был ли квест добавлен в журнал игры
        /// </summary>
        public bool AddedToGameLog { get; set; } = false;

        /// <summary>
        /// Был ли дебафф вылечен
        /// </summary>
        public bool IsCured { get; set; } = false;

        /// <summary>
        /// Был ли квест завершен
        /// </summary>
        public bool IsCompleted { get; set; } = false;

        // ===== ПРОГРЕСС ЛЕЧЕНИЯ =====
        
        /// <summary>
        /// Прогресс лечения (счетчики для разных типов дебаффов)
        /// </summary>
        public TreatmentProgress Progress { get; set; } = new();

        // ===== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ =====

        /// <summary>
        /// Проверяет, нужно ли восстанавливать этот дебафф
        /// </summary>
        public bool ShouldRestore(SDate currentDate)
        {
            // Если вылечен - не восстанавливать
            if (IsCured)
                return false;

            // Если прошло больше 7 дней - считаем устаревшим
            int daysSinceIssued = currentDate.DaysSinceStart - IssuedDate.DaysSinceStart;
            if (daysSinceIssued > 7)
                return false;

            return true;
        }

        /// <summary>
        /// Проверяет, активно ли лечение (квест выдан и не завершен)
        /// </summary>
        public bool IsTreatmentActive()
        {
            return TreatmentStarted && !IsCompleted && !IsCured;
        }

        /// <summary>
        /// Проверяет, можно ли завершить лечение
        /// </summary>
        public bool CanCompleteTreatment()
        {
            return IsTreatmentActive() && Progress != null;
        }

        /// <summary>
        /// Возвращает текстовое описание текущего статуса
        /// </summary>
        public string GetStatusDescription()
        {
            if (IsCured)
                return $"✅ Вылечен ({CompletedDate?.ToString() ?? "неизвестно"})";
            
            if (IsCompleted)
                return $"🎯 Квест завершен ({CompletedDate?.ToString() ?? "неизвестно"})";
            
            if (TreatmentStarted)
                return $"🔄 Лечение активно ({TreatmentStartedDate?.ToString() ?? "неизвестно"})";
            
            return $"⚠️ Дебафф активен ({IssuedDate})";
        }

        /// <summary>
        /// ⭐ НОВОЕ: Генерирует уникальный ключ для лечения
        /// </summary>
        public static string GenerateTreatmentKey(string buffId, int instanceNumber = 1)
        {
            return $"{buffId}_{instanceNumber}";
        }

        /// <summary>
        /// ⭐ НОВОЕ: Генерирует уникальный ключ с временной меткой
        /// </summary>
        public static string GenerateTreatmentKeyWithTimestamp(string buffId)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return $"{buffId}_{timestamp}";
        }

        /// <summary>
        /// ⭐ НОВОЕ: Инициализирует уникальный ключ если он не установлен
        /// </summary>
        public void EnsureTreatmentKey()
        {
            if (string.IsNullOrEmpty(TreatmentKey))
            {
                TreatmentKey = GenerateTreatmentKey(BuffId, InstanceNumber);
            }
        }

        /// <summary>
        /// ⭐ НОВОЕ: Проверяет, является ли это лечение уникальным экземпляром
        /// </summary>
        public bool IsUniqueInstance()
        {
            return !string.IsNullOrEmpty(TreatmentKey) && TreatmentKey.Contains("_");
        }
    }
}
