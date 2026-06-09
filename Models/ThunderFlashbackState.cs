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

        /// <summary>Харви стабилизировал эпизод сегодня (grace / anchor window).</summary>
        public bool IsStabilizedByHarveyToday { get; set; }

        /// <summary>Время стабилизации Харви (Game1.timeOfDay).</summary>
        public int StabilizedByHarveyAtTime { get; set; }

        /// <summary>До этого времени relapse подавлен в anchor-локациях и рядом с Харви.</summary>
        public int HarveyAnchorGraceUntil { get; set; }

        /// <summary>До этого времени повторный relapse не проверяется.</summary>
        public int ThunderRelapseCooldownUntil { get; set; }

        /// <summary>Игрок покинул anchor после стабилизации Харви.</summary>
        public bool LeftHarveyAnchorAfterStabilization { get; set; }

        /// <summary>Тяжёлый (primary/Gotoro) flashback уже был сегодня.</summary>
        public bool WasPrimaryFlashbackTriggeredToday { get; set; }

        /// <summary>Лёгкий relapse уже был сегодня.</summary>
        public bool WasRelapseTriggeredToday { get; set; }

        /// <summary>Скрытая чувствительность к грозе (дней); уменьшается по дням, не сбрасывается мгновенно.</summary>
        public int ThunderSensitivityDays { get; set; }

        /// <summary>Время последнего relapse или предупреждения (Game1.timeOfDay).</summary>
        public int LastRelapseTime { get; set; }

        /// <summary>Локация, где Харви стабилизировал эпизод.</summary>
        public string? StabilizedByHarveyLocation { get; set; }
    }
}
