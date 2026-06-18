using StardewModdingAPI.Utilities;

namespace HarveyStressMeter.Models
{
    public enum HandbookTab { Inventory, Skills }

    public sealed class ModConfig
    {
        /// <summary>Устарело: hotkey H перенесён в HarveyOverhaul.Core (OpenHarveyPanel).</summary>
        public KeybindList OpenHandbook { get; set; } = new();
        public HandbookTab ButtonOn { get; set; } = HandbookTab.Inventory;

        /// <summary>
        /// DEV/TEST: регистрирует консольные команды hs.test.* и stress_* (при EnableDevTestCommands=true).
        /// </summary>
        public bool EnableDevTestCommands { get; set; } = false;

        /// <summary>HTTP MCP server для AI-тестирования (порт 24844 по умолчанию).</summary>
        public bool EnableStressMcp { get; set; } = true;

        /// <summary>Port for Stress MCP (default 24844; StardewMCP uses 24842, Injury MCP 24843).</summary>
        public int StressMcpPort { get; set; } = 24844;

        // --- Stress Meter HUD ---

        /// <summary>Master switch for the in-game stress meter.</summary>
        public bool ShowStressMeter { get; set; } = true;

        /// <summary>When true, hide the meter below Mild threshold unless episode/debug rules apply.</summary>
        public bool ShowOnlyWhenStressed { get; set; } = true;

        /// <summary>Show numeric StressLoad on the compact meter.</summary>
        public bool ShowDebugNumbers { get; set; } = false;

        /// <summary>Persistent debug overlay with causes/episodes (also toggled in-game).</summary>
        public bool ShowDebugOverlay { get; set; } = false;

        /// <summary>Short HUD messages when stress tier/load crosses thresholds.</summary>
        public bool EnableHudMessages { get; set; } = true;

        public StressHudAnchor Anchor { get; set; } = StressHudAnchor.BottomRight;

        /// <summary>Vertical bar left of vanilla stamina (recommended for BottomRight).</summary>
        public bool VerticalStressMeter { get; set; } = true;

        /// <summary>Margin from the right UI edge (stamina/toolbar reserve when vertical).</summary>
        public int OffsetX { get; set; } = 112;

        /// <summary>Margin from the bottom of the viewport.</summary>
        public int OffsetY { get; set; } = 16;

        public float Scale { get; set; } = 1f;

        /// <summary>0–1 opacity for meter chrome.</summary>
        public float Opacity { get; set; } = 0.85f;

        /// <summary>Toggle runtime debug overlay (LeftAlt+S).</summary>
        public KeybindList StressMeterDebugToggle { get; set; } = KeybindList.Parse("LeftAlt + S");

        // --- StressLoad balance ---

        /// <summary>StressLoad ≥ this → Mild.</summary>
        public int MildThreshold { get; set; } = 25;

        /// <summary>StressLoad ≥ this → High.</summary>
        public int HighThreshold { get; set; } = 50;

        /// <summary>StressLoad ≥ this → Critical.</summary>
        public int CriticalThreshold { get; set; } = 75;

        /// <summary>Upper cap for StressLoad scale.</summary>
        public int MaxStressLoad { get; set; } = 100;

        /// <summary>Passive StressLoad decay per in-game hour (resting contexts).</summary>
        public float StressDecayPerHour { get; set; } = 2f;

        /// <summary>Max MaxStamina penalty points from consolidated stress (cap).</summary>
        public int MaxStaminaPenalty { get; set; } = 25;

        /// <summary>Max Speed penalty (absolute, e.g. 1 = -1 speed).</summary>
        public int MaxSpeedPenalty { get; set; } = 1;

        /// <summary>Tier stamina penalty at Mild (before cap/multiplier).</summary>
        public int MildStaminaPenalty { get; set; } = 6;

        /// <summary>Tier stamina penalty at High.</summary>
        public int HighStaminaPenalty { get; set; } = 15;

        /// <summary>Tier stamina penalty at Critical.</summary>
        public int CriticalStaminaPenalty { get; set; } = 22;

        /// <summary>Tier speed penalty at Critical only.</summary>
        public int CriticalSpeedPenalty { get; set; } = 1;

        /// <summary>
        /// When true, cause buffs stay visible but mechanical effects come only from tier buff (capped).
        /// </summary>
        public bool ConsolidateStressPenalties { get; set; } = true;

        /// <summary>Balanced / StoryFocus (no penalties) / Survival (+15% penalties).</summary>
        public StressGameplayMode GameplayMode { get; set; } = StressGameplayMode.Balanced;

        // --- LightningFright ---

        public int LightningFrightBaseChance { get; set; } = 5;

        public int LightningFrightCooldownMinMinutes { get; set; } = 30;

        public int LightningFrightCooldownMaxMinutes { get; set; } = 60;

        public int ForestShelterRequiredSeconds { get; set; } = 90;

        public int GotoroForestShelterBonusSeconds { get; set; } = 30;

        /// <summary>Grace period (игровые минуты) после стабилизации Харви — relapse подавлен.</summary>
        public int HarveyStabilizationGraceMinutes { get; set; } = 120;

        /// <summary>Cooldown (игровые минуты) между проверками thunder relapse.</summary>
        public int ThunderRelapseCooldownMinutes { get; set; } = 60;

        /// <summary>Дней скрытой чувствительности к грозе после помощи Харви.</summary>
        public int ThunderSensitivityDaysAfterHarveyCare { get; set; } = 3;

        // --- Harvey Gotoro forest rescue ---

        public bool EnableHarveyFlashbackRescue { get; set; } = true;

        public int MinHeartsForForestRescue { get; set; } = 6;

        public int MinForestSecondsBeforeRescue { get; set; } = 30;

        public double RescueChanceHearts6To7 { get; set; } = 0.35;

        public double RescueChanceHearts8To10 { get; set; } = 0.65;

        public double RescueChanceDating { get; set; } = 0.85;

        public double RescueChanceMarried { get; set; } = 1.0;

        public int MaxRescuesPerDay { get; set; } = 1;

        public int RescueCooldownDays { get; set; } = 2;

        /// <summary>Женаты на Харви — игнорировать RescueCooldownDays.</summary>
        public bool MarriedIgnoresCooldown { get; set; } = true;

        // --- HarveyCareTrust ---

        public int MaxHarveyCareTrustPoints { get; set; } = 250;

        public int TrustIgnoredAssignmentDays { get; set; } = 7;

        public int TrustPenaltyCooldownDays { get; set; } = 4;

        public int MaxTrustPenaltyPerEvent { get; set; } = 5;

        // --- Harvey safe person aura (рядом с Харви легче) ---

        public bool EnableHarveySafePersonAura { get; set; } = true;

        public float HarveySafeDistanceTiles { get; set; } = 6f;

        public int SafeAuraDecayIntervalMinutes { get; set; } = 10;

        public int SafeAuraStressReductionLevel3 { get; set; } = 1;

        public int SafeAuraStressReductionLevel4 { get; set; } = 2;

        public int DatingSafeAuraBonus { get; set; } = 1;

        public int MarriedSafeAuraBonus { get; set; } = 1;

        public bool ShowSafeAuraMessages { get; set; } = true;

        public int SafeAuraMessageCooldownMinutes { get; set; } = 120;

        public bool PenaltiesEnabled =>
            GameplayMode != StressGameplayMode.StoryFocus;

        public float PenaltyStrengthMultiplier => GameplayMode switch
        {
            StressGameplayMode.Survival => 1.15f,
            StressGameplayMode.StoryFocus => 0f,
            _ => 1f,
        };

        /// <summary>Устарело: используйте <see cref="MedicalLetters"/>.</summary>
        public bool SendLetters
        {
            get => MedicalLetters != MedicalLetterMode.Off;
            set => MedicalLetters = value ? MedicalLetterMode.All : MedicalLetterMode.Off;
        }

        public bool SendStoryLetters { get; set; } = true;
        public bool SendRomanticCareLetters { get; set; } = true;
        public MedicalLetterMode MedicalLetters { get; set; } = MedicalLetterMode.CriticalOnly;
    }
}

