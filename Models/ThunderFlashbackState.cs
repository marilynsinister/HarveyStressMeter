namespace HarveyStressMeter.Models
{
    /// <summary>Состояние испуга молнии / Gotoro flashback во время грозы.</summary>
    public sealed class ThunderFlashbackState
    {
        public bool IsActive { get; set; }
        public bool WasTriggeredToday { get; set; }
        public bool WasStabilizedToday { get; set; }
        public bool EnteredForestDuringFlashback { get; set; }
        public int ForestShelterSeconds { get; set; }
        public int RequiredForestShelterSeconds { get; set; } = 90;
        public string? TriggerLocation { get; set; }
        public int TriggerTime { get; set; }
        public int LastFrightCheckTime { get; set; }
        public int LastHudMessageTime { get; set; }
        /// <summary>Следующий cooldown (игровые минуты) после последнего ambient HUD.</summary>
        public int HudMessageCooldownMinutes { get; set; } = 45;
        public int LightningFrightIntensity { get; set; }
        public bool IsGotoroFlashback { get; set; }

        /// <summary>Укрытие в лесу до старта Gotoro episode-квеста (переносится в progress).</summary>
        public int DeferredGotoroShelterSeconds { get; set; }
    }
}
