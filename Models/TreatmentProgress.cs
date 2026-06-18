using System.Collections.Generic;
using HarveyStressMeter.Helpers;
using StardewModdingAPI.Utilities;
using StardewValley;

namespace HarveyStressMeter.Models
{
    /// <summary>
    /// Прогресс лечения конкретного состояния
    /// </summary>
    public sealed class TreatmentProgress
    {
        public string QuestId { get; set; } = "";
        public SDate StartedOn { get; set; } = SDate.Now();

        /// <summary>
        /// До этого тика Game1.ticks нельзя завершать квест (защита от мгновенного complete при старте лечения).
        /// -1 = ограничение отключено (старые сейвы).
        /// </summary>
        public int QuestObjectivesEnabledAfterTick { get; set; } = -1;

        public void BeginQuestObjectivesGracePeriod(int delayTicks = 180)
            => QuestObjectivesEnabledAfterTick = Game1.ticks + delayTicks;

        public bool CanEvaluateQuestObjectives()
            => QuestObjectivesEnabledAfterTick < 0 || Game1.ticks >= QuestObjectivesEnabledAfterTick;

        // ===== Гроза (Thunder) =====
        /// <summary>
        /// Секунды, проведенные рядом с Харви во время грозы
        /// </summary>
        public int SecondsNearHarvey { get; set; } = 0;      // гроза
        // ===== Темнота (Darkness) =====
        /// <summary>
        /// Legacy: секунды в светлой локации (только buffStressDarkness без уровневой системы).
        /// Шаг 1 уровневой терапии — DarknessProgress.SafeDarknessProgressToday / DarknessService.
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

        // ===== SocialShutdown (эпизод «Не оставаться одной») =====
        /// <summary>Разговор с доверенным NPC (≥4 сердечка) или с Харви после выдачи квеста.</summary>
        public bool SocialShutdownTrustedTalk { get; set; }

        /// <summary>Уникальные малознакомые NPC, с которыми говорили сегодня (лимит в день).</summary>
        public HashSet<string> SocialShutdownUnfamiliarNpcs { get; set; } = new();

        /// <summary>Выполненные подзадачи episode PhysicalExhaustion (StressCause id).</summary>
        public HashSet<string> EpisodeCausesCompleted { get; set; } = new();

        /// <summary>Burnout: true, пока игрок не заходил в шахты сегодня.</summary>
        public bool BurnoutAvoidedMinesToday { get; set; } = true;

        /// <summary>AnxietySpike: секунды в безопасной локации.</summary>
        public int AnxietySafeSeconds { get; set; }

        /// <summary>AnxietySpike: HUD/journal completion уже объявлены (защита от пропуска при скачке счётчика).</summary>
        public bool AnxietySpikeCompletionAnnounced { get; set; }


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
        /// Секунды «правильного» отдыха дома (FarmHouse, без тяжёлых инструментов)
        /// </summary>
        public int TiredRestSeconds { get; set; }
        /// <summary>
        /// Минуты отдыха для лечения усталости (legacy, не используется в tick-path)
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

        public int SocialShutdownUnfamiliarCount => SocialShutdownUnfamiliarNpcs.Count;

        public bool IsSocialShutdownHarveyPathComplete() =>
            SecondsNearHarvey >= SocialShutdownQuestHelper.HarveySecondsRequired;

        public bool IsSocialShutdownTrustedPathComplete() =>
            SocialShutdownTrustedTalk
            && SocialShutdownUnfamiliarCount <= SocialShutdownQuestHelper.MaxUnfamiliarTalksPerDay;

        public bool IsSocialShutdownQuestCompleted() =>
            IsSocialShutdownHarveyPathComplete() || IsSocialShutdownTrustedPathComplete();

        public bool IsSocialShutdownOverloaded() =>
            SocialShutdownUnfamiliarCount > SocialShutdownQuestHelper.MaxUnfamiliarTalksPerDay;

        public string GetSocialShutdownProgressText()
        {
            if (IsSocialShutdownQuestCompleted())
            {
                if (IsSocialShutdownHarveyPathComplete())
                    return "✅ Задача выполнена! (60 сек рядом с Харви)";

                return "✅ Задача выполнена! (разговор с доверенным человеком)";
            }

            var harveyLine = $"Рядом с Харви: {System.Math.Min(SecondsNearHarvey, SocialShutdownQuestHelper.HarveySecondsRequired)}/{SocialShutdownQuestHelper.HarveySecondsRequired} сек";
            var trustedLine = SocialShutdownTrustedTalk
                ? "Доверенный контакт: ✅"
                : "Доверенный контакт: поговорите с другом от 4 сердечек";
            var unfamiliarLine =
                $"Малознакомые сегодня: {SocialShutdownUnfamiliarCount}/{SocialShutdownQuestHelper.MaxUnfamiliarTalksPerDay}";

            if (IsSocialShutdownOverloaded())
            {
                return $"{harveyLine}\n{trustedLine}\n⚠️ Слишком много разговоров с малознакомыми — сегодня путь через доверие закрыт. Побудьте рядом с Харви.";
            }

            return $"{harveyLine}\n{trustedLine}\n{unfamiliarLine}";
        }
    }
}

