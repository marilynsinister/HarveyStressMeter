using StardewModdingAPI.Utilities;

namespace HarveyStressMeter.Models
{
    /// <summary>
    /// Прогресс лечения конкретного состояния
    /// </summary>
    public sealed class TreatmentProgress
    {
        public string QuestId { get; set; } = "";
        public SDate StartedOn { get; set; } = SDate.Now();

        // ===== Гроза (Thunder) =====
        /// <summary>
        /// Секунды, проведенные рядом с Харви во время грозы
        /// </summary>
        public int SecondsNearHarvey { get; set; } = 0;      // гроза
        // ===== Темнота (Darkness) =====
        /// <summary>
        /// Секунды, проведенные в помещении вечером при свете
        /// </summary>
        public int EveningInLightSeconds { get; set; } = 0;  // темнота

        // ===== Одиночество (Lonely) =====
        /// <summary>
        /// Количество уникальных разговоров сегодня (для квеста Lonely)
        /// </summary>
        public int TalkedUniqueToday { get; set; } = 0;      // одиночество

        // ===== Социальная тревожность (Social) =====
        /// <summary>
        /// ⭐ НОВОЕ: Базовое количество разговоров при получении квеста Social
        /// Используется для расчета разговоров ПОСЛЕ получения квеста
        /// Инициализируется в TreatmentService.StartTreatment()
        /// 
        /// Пример: Если игрок поговорил с 2 NPC до получения квеста,
        /// то TalkedUniqueToday = 2
        /// </summary>
        // ПРИМЕЧАНИЕ: Для Social квеста TalkedUniqueToday используется
        // как БАЗА для подсчета разговоров после получения квеста

        /// <summary>
        /// ⭐ НОВОЕ: Счетчик разговоров ПОСЛЕ получения квеста Social
        /// Обновляется в ModEntry.UpdateSocialQuestProgress()
        /// 
        /// Расчет: SocialTalksAfterQuest = TalkedNpcsToday.Count - TalkedUniqueToday
        /// 
        /// Пример: 
        /// - База при получении квеста: TalkedUniqueToday = 2 (поговорили с 2 NPC до квеста)
        /// - Текущее количество: TalkedNpcsToday.Count = 5 (всего 5 разговоров сегодня)
        /// - Результат: SocialTalksAfterQuest = 5 - 2 = 3 (3 разговора ПОСЛЕ квеста)
        /// </summary>
        public int SocialTalksAfterQuest { get; set; } = 0;


        // ===== Голод (Hunger) =====
        /// <summary>
        /// Флаг: съедена ли любая еда
        /// </summary>
        public bool AteAnyFood { get; set; } = false;        // голод

        // ===== Холод (TooCold) =====
        /// <summary>
        /// Секунды, проведенные в теплой зоне
        /// </summary>  
        public int WarmSeconds { get; set; } = 0;            // холод
                                                             // ===== Недосып (NoSleep) =====
        /// <summary>
        /// Серия ранних отбоев (до 22:00)
        /// </summary>
        public int EarlySleepStreak { get; set; } = 0;       // недосып: серия ранних отбоев

        // ===== Усталость (Tired) =====
        /// <summary>
        /// Минуты отдыха для лечения усталости
        /// </summary>
        public int TiredRestMinutes { get; set; }
        /// <summary>
        /// Последнее время дня при проверке усталости
        /// </summary>
        public int? TiredLastTimeOfDay { get; set; }

        // ===== Вспомогательные методы =====

        /// <summary>
        /// ⭐ НОВОЕ: Возвращает текстовое описание прогресса для Social квеста
        /// </summary>
        public string GetSocialProgressText()
        {
            // Вариант 1: 3 разговора + 60 сек с Харви (оба условия выполнены)
            if (SocialTalksAfterQuest >= 3 && SecondsNearHarvey >= 60)
            {
                return "✅ Задача выполнена! (3 разговора + время с Харви)";
            }

            // Вариант 2: 5 разговоров (альтернативное условие)
            if (SocialTalksAfterQuest >= 5)
            {
                return "✅ Задача выполнена! (5 разговоров)";
            }

            // Показываем прогресс для обоих путей
            if (SocialTalksAfterQuest >= 3)
            {
                return $"Поговорили с персонажами: {SocialTalksAfterQuest}/3 ✅ | Время с Харви: {SecondsNearHarvey}/60 сек";
            }

            // Обычный прогресс
            return $"Поговорили с персонажами: {SocialTalksAfterQuest}/3 (или 5) | Время с Харви: {SecondsNearHarvey}/60 сек";
        }

        /// <summary>
        /// ⭐ НОВОЕ: Проверяет, выполнены ли условия для завершения Social квеста
        /// </summary>
        public bool IsSocialQuestCompleted()
        {
            // Путь 1: 3 разговора + 60 сек с Харви
            bool path1 = SocialTalksAfterQuest >= 3 && SecondsNearHarvey >= 60;

            // Путь 2: 5 разговоров
            bool path2 = SocialTalksAfterQuest >= 5;

            return path1 || path2;
        }

        /// <summary>
        /// ⭐ НОВОЕ: Возвращает, какой путь был выбран для завершения квеста
        /// </summary>
        public string GetSocialCompletionPath()
        {
            if (SocialTalksAfterQuest >= 3 && SecondsNearHarvey >= 60)
            {
                return "path1"; // 3 разговора + время с Харви
            }

            if (SocialTalksAfterQuest >= 5)
            {
                return "path2"; // 5 разговоров
            }

            return "none"; // Не завершен
        }
    }
}

