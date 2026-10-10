using System.Collections.Generic;

namespace HarveyStressMeter.Models
{
    /// <summary>Дневной учёт «занятий для души», снижающих StressLoad (убывающая отдача).</summary>
    public sealed class ComfortActivityState
    {
        /// <summary>DaysPlayed, к которому относятся счётчики.</summary>
        public int Day { get; set; } = -1;

        /// <summary>Сколько раз сегодня сработало каждое занятие (ComfortActivities.*).</summary>
        public Dictionary<string, int> UsesToday { get; set; } = new();

        /// <summary>Суммарное снижение StressLoad от занятий за день.</summary>
        public int ReliefToday { get; set; }

        /// <summary>Игровые минуты в «тихом» месте сегодня (закат, салун) по типу занятия.</summary>
        public Dictionary<string, int> DwellMinutesToday { get; set; } = new();

        /// <summary>Сколько животных уже поглажено в локации при последней проверке (локация → число).</summary>
        public Dictionary<string, int> PettedAnimalsSeen { get; set; } = new();

        public uint LastFishCaught { get; set; }

        public uint LastItemsForaged { get; set; }
    }
}
