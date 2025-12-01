using System.Collections.Generic;
using StardewModdingAPI.Utilities;

namespace HarveyStressMeter.Models
{
    /// <summary>
    /// Прогресс для дебаффа "Боязнь темноты" с системой уровней страха
    /// </summary>
    public class DarknessProgress
    {
        // ===== Уровень страха =====
        /// <summary>
        /// Текущий уровень страха: 0 (норма), 1 (легкий), 2 (средний), 3 (фобия)
        /// </summary>
        public int FearLevel { get; set; } = 0;
        
        // ===== История эпизодов =====
        /// <summary>
        /// Общее количество эпизодов страха за всё время
        /// </summary>
        public int DarknessEpisodesCount { get; set; } = 0;
        
        /// <summary>
        /// Дата последнего эпизода
        /// </summary>
        public SDate? LastEpisodeDate { get; set; }
        
        /// <summary>
        /// Количество эпизодов за эту неделю
        /// </summary>
        public int EpisodesThisWeek { get; set; } = 0;
        
        /// <summary>
        /// Дата начала текущей недели (для подсчета еженедельных эпизодов)
        /// </summary>
        public SDate? WeekStartDate { get; set; }
        
        // ===== Игнорирование =====
        /// <summary>
        /// Дата, когда начали игнорировать (появился топик, но не начали лечение)
        /// </summary>
        public SDate? IgnoredSinceDate { get; set; }
        
        /// <summary>
        /// Сколько дней игнорируется
        /// </summary>
        public int DaysIgnored { get; set; } = 0;
        
        /// <summary>
        /// Дата последнего повышения уровня страха
        /// </summary>
        public SDate? LastLevelIncreaseDate { get; set; }
        
        // ===== Прогресс лечения =====
        /// <summary>
        /// Шаг 1: Минуты при приглушенном свете (0-15)
        /// </summary>
        public int SafeDarknessMinutes { get; set; } = 0;
        
        /// <summary>
        /// Шаг 2: Посещенные безопасные зоны
        /// </summary>
        public List<string> SafeZonesVisited { get; set; } = new();
        
        /// <summary>
        /// Шаг 3: Секунды в горах ночью (0-120)
        /// </summary>
        public int MountainNightSeconds { get; set; } = 0;
        
        /// <summary>
        /// Текущий этап терапии: 0 (не начата), 1-3 (шаги)
        /// </summary>
        public int TherapyStage { get; set; } = 0;
        
        // ===== Статус =====
        /// <summary>
        /// Терапия активна
        /// </summary>
        public bool IsTherapyActive { get; set; } = false;
        
        /// <summary>
        /// Полностью излечен
        /// </summary>
        public bool IsCured { get; set; } = false;
        
        /// <summary>
        /// Получен перманентный бонус "Преодоление"
        /// </summary>
        public bool HasOvercomeBonus { get; set; } = false;
        
        /// <summary>
        /// Дата начала терапии
        /// </summary>
        public SDate? TherapyStartDate { get; set; }
        
        /// <summary>
        /// Дата завершения терапии
        /// </summary>
        public SDate? TherapyCompletedDate { get; set; }
        
        // ===== Флаги для предметов =====
        /// <summary>
        /// Игрок получил диммер от Харви
        /// </summary>
        public bool HasReceivedDimmer { get; set; } = false;
        
        /// <summary>
        /// Игрок получил фонарь от Харви
        /// </summary>
        public bool HasReceivedLantern { get; set; } = false;
        
        /// <summary>
        /// Бафф "Поддержка Харви" активен (виртуальное сопровождение)
        /// </summary>
        public bool HasHarveySupportBuff { get; set; } = false;
        
        // ===== Достижения =====
        /// <summary>
        /// Игрок выполнил Шаг 1 (безопасная темнота)
        /// </summary>
        public bool CompletedStep1 { get; set; } = false;
        
        /// <summary>
        /// Игрок выполнил Шаг 2 (контролируемая прогулка)
        /// </summary>
        public bool CompletedStep2 { get; set; } = false;
        
        /// <summary>
        /// Игрок выполнил Шаг 3 (ночь в горах)
        /// </summary>
        public bool CompletedStep3 { get; set; } = false;
        
        // ===== Методы =====
        
        /// <summary>
        /// Обновить счетчик эпизодов за неделю
        /// </summary>
        public void UpdateWeeklyCounter(SDate currentDate)
        {
            if (WeekStartDate == null)
            {
                WeekStartDate = currentDate;
                EpisodesThisWeek = 0;
                return;
            }
            
            // Если прошло 7+ дней, сбрасываем счетчик
            int daysSinceWeekStart = currentDate.DaysSinceStart - WeekStartDate.DaysSinceStart;
            if (daysSinceWeekStart >= 7)
            {
                WeekStartDate = currentDate;
                EpisodesThisWeek = 0;
            }
        }
        
        /// <summary>
        /// Зарегистрировать новый эпизод страха
        /// </summary>
        public void RegisterEpisode(SDate currentDate)
        {
            DarknessEpisodesCount++;
            LastEpisodeDate = currentDate;
            
            UpdateWeeklyCounter(currentDate);
            EpisodesThisWeek++;
        }
        
        /// <summary>
        /// Обновить счетчик игнорирования
        /// </summary>
        public void UpdateIgnoredDays(SDate currentDate)
        {
            if (IgnoredSinceDate == null && FearLevel > 0 && !IsTherapyActive)
            {
                IgnoredSinceDate = currentDate;
                DaysIgnored = 0;
            }
            else if (IgnoredSinceDate != null && !IsTherapyActive)
            {
                DaysIgnored = currentDate.DaysSinceStart - IgnoredSinceDate.DaysSinceStart;
            }
            else if (IsTherapyActive)
            {
                // Если начали лечение, сбрасываем игнорирование
                IgnoredSinceDate = null;
                DaysIgnored = 0;
            }
        }
        
        /// <summary>
        /// Проверить, нужно ли повысить уровень страха
        /// </summary>
        public bool ShouldIncreaseFearLevel(SDate currentDate)
        {
            // Уровень 0 → 1: автоматически при первом эпизоде (обрабатывается в триггере)
            
            // Уровень 1 → 2
            if (FearLevel == 1)
            {
                // Через 5-7 дней игнорирования
                if (DaysIgnored >= 5) return true;
                
                // ИЛИ 3+ эпизода за неделю
                if (EpisodesThisWeek >= 3) return true;
            }
            
            // Уровень 2 → 3
            if (FearLevel == 2)
            {
                // Через 7 дней игнорирования
                if (DaysIgnored >= 7) return true;
                
                // ИЛИ 5+ эпизодов за неделю
                if (EpisodesThisWeek >= 5) return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Повысить уровень страха
        /// </summary>
        public void IncreaseFearLevel(SDate currentDate)
        {
            if (FearLevel < 3)
            {
                FearLevel++;
                LastLevelIncreaseDate = currentDate;
            }
        }
        
        /// <summary>
        /// Проверить, можно ли снизить уровень страха естественным путем
        /// </summary>
        public bool CanDecreaseFearNaturally(SDate currentDate)
        {
            // Только для уровня 1
            if (FearLevel != 1) return false;
            
            // Если нет эпизодов 3+ дня
            if (LastEpisodeDate == null) return false;
            
            int daysSinceLastEpisode = currentDate.DaysSinceStart - LastEpisodeDate.DaysSinceStart;
            return daysSinceLastEpisode >= 3;
        }
        
        /// <summary>
        /// Снизить уровень страха естественным путем
        /// </summary>
        public void DecreaseFearLevel()
        {
            if (FearLevel > 0)
            {
                FearLevel--;
                
                // Если уровень снизился до 0, сбрасываем прогресс
                if (FearLevel == 0)
                {
                    ResetProgress();
                }
            }
        }
        
        /// <summary>
        /// Сбросить прогресс (после естественного излечения или завершения терапии)
        /// </summary>
        public void ResetProgress()
        {
            EpisodesThisWeek = 0;
            IgnoredSinceDate = null;
            DaysIgnored = 0;
        }
        
        /// <summary>
        /// Получить название текущего баффа в зависимости от уровня страха
        /// </summary>
        public string GetCurrentBuffId()
        {
            return FearLevel switch
            {
                1 => Constants.BuffIds.Darkness + "Level1",
                2 => Constants.BuffIds.Darkness + "Level2",
                3 => Constants.BuffIds.Darkness + "Level3",
                _ => Constants.BuffIds.Darkness // fallback на старый
            };
        }
        
        /// <summary>
        /// Получить человекочитаемое описание текущего уровня страха
        /// </summary>
        public string GetFearLevelDescription()
        {
            return FearLevel switch
            {
                0 => "Нормально",
                1 => "Легкое беспокойство",
                2 => "Растущий страх",
                3 => "Фобия",
                _ => "Неизвестно"
            };
        }
    }
}
