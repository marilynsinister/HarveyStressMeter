namespace HarveyStressMeter.Models
{
    /// <summary>Состояние поиска игрока Харви в лесу во время Gotoro flashback.</summary>
    public sealed class HarveyFlashbackRescueState
    {
        public bool HarveyRescueTriggeredToday { get; set; }

        /// <summary>Absolute day (Game1.Date.TotalDays) последнего успешного rescue.</summary>
        public int LastRescueDay { get; set; }

        public string? LastRescueEventId { get; set; }

        /// <summary>Секунды в лесу с начала текущего flashback (для rescue gate).</summary>
        public int ForestSecondsBeforeRescue { get; set; }

        public bool HarveyHelpedStabilizeToday { get; set; }

        public int LastRescueCheckTime { get; set; }

        public string? RescueTier { get; set; }

        /// <summary>Tier, ожидающий post-rescue эффектов после завершения event.</summary>
        public string? PendingPostRescueTier { get; set; }

        public string? PendingPostRescueEventId { get; set; }
    }
}
