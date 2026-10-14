using System.Collections.Generic;

namespace HarveyStressMeter.Models
{
    /// <summary>Общее состояние шкалы стресса игрока.</summary>
    public sealed class StressLoadState
    {
        public int CurrentStressLoad { get; set; }

        /// <summary>«Хорошая неделя»: 1 — день закончился спокойно (шкала ниже Mild), 0 — нет. Последние 7 дней.</summary>
        public List<int> CalmDayHistory { get; set; } = new();

        /// <summary>День (DaysPlayed) последней награды «Хорошая неделя».</summary>
        public int LastGoodWeekRewardDay { get; set; } = -100;

        /// <summary>Снижение шкалы от отдыха/лечения/ауры при сохранении активных causes.</summary>
        public int StressRecoveryOffset { get; set; }

        /// <summary>Последний пересчитанный load из causes (до recovery offset).</summary>
        public int LastCauseLoad { get; set; }

        public StressSeverity Severity { get; set; } = StressSeverity.Calm;
        public Dictionary<string, StressCauseState> ActiveCauses { get; set; } = new();
        public string? ActiveEpisodeId { get; set; }
        public string? ActiveTreatmentEpisodeId { get; set; }
        public bool HasActiveTreatment { get; set; }
        public bool AwaitingHarveyReview { get; set; }
        public int LastUpdatedTime { get; set; }
        public string? LastPrimaryCause { get; set; }

        /// <summary>Флаг flashback-сценария (может быть выставлен внешней системой).</summary>
        public bool GotoroFlashbackActive { get; set; }

        /// <summary>Хроническая чувствительность к war trauma (триггер GotoroFlashback episode).</summary>
        public bool WarTraumaFlag { get; set; }
    }
}
