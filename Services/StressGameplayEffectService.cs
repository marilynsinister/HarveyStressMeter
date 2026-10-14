using System;
using System.Collections.Generic;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Models;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buffs;

namespace HarveyStressMeter.Services
{
    /// <summary>
    /// Capped gameplay-штрафы от StressLoad severity.
    /// Один tier-buff вместо линейного стака всех cause debuffs.
    /// </summary>
    public sealed class StressGameplayEffectService
    {
        private static readonly string[] MechanicalStressBuffIds =
        {
            BuffIds.Tired,
            BuffIds.Lonely,
            BuffIds.Thunder,
            BuffIds.Hunger,
            BuffIds.Overwork,
            BuffIds.NoSleep,
            BuffIds.TooCold,
            BuffIds.Social,
            BuffIds.Panic,
            BuffIds.DarknessLevel1,
            BuffIds.DarknessLevel2,
            BuffIds.DarknessLevel3,
        };

        private readonly ModConfig _config;
        private readonly StressLoadService _stressLoadService;
        private readonly BuffService _buffService;
        private readonly SaveData _data;
        private readonly IMonitor _monitor;

        private StressSeverity _lastSeverity = StressSeverity.Calm;
        private int _lastAppliedStaminaPenalty;
        private int _lastAppliedSpeedPenalty;
        private int _bedTime = -1;
        private StressSeverity _severityAtBed = StressSeverity.Calm;

        /// <summary>Шкала не выше этого — «Ясная голова» (+1 удача).</summary>
        private const int ClearMindMaxLoad = 10;
        /// <summary>Лёг до этого часа — утром стресс заметно ниже.</summary>
        private const int EarlyBedTime = 2300;
        private const int EarlySleepRelief = 10;
        /// <summary>Критический стресс и сон после полуночи — проснёшься не полностью отдохнувшей.</summary>
        private const float RestlessSleepStaminaFactor = 0.85f;

        public StressGameplayEffectService(
            ModConfig config,
            StressLoadService stressLoadService,
            BuffService buffService,
            SaveData data,
            IMonitor monitor)
        {
            _config = config;
            _stressLoadService = stressLoadService;
            _buffService = buffService;
            _data = data;
            _monitor = monitor;
        }

        public (int StaminaPenalty, int SpeedPenalty) LastAppliedPenalties =>
            (_lastAppliedStaminaPenalty, _lastAppliedSpeedPenalty);

        public void UpdateEffects()
        {
            if (!Context.IsWorldReady)
                return;

            if (!_config.PenaltiesEnabled)
            {
                ClearTierBuff();
                SetClearMind(false);
                return;
            }

            var severity = _stressLoadService.GetSeverity();
            SetClearMind(severity == StressSeverity.Calm && _stressLoadService.GetCurrentStressLoad() <= ClearMindMaxLoad);

            if (severity == StressSeverity.Calm)
            {
                ClearTierBuff();
                _lastSeverity = severity;
                _lastAppliedStaminaPenalty = 0;
                _lastAppliedSpeedPenalty = 0;
                return;
            }

            if (_config.ConsolidateStressPenalties)
                ApplyCosmeticStressBuffs();

            var (staminaPenalty, speedPenalty) = ComputeCappedPenalties(severity);

            if (staminaPenalty == _lastAppliedStaminaPenalty
                && speedPenalty == _lastAppliedSpeedPenalty
                && severity == _lastSeverity)
            {
                return;
            }

            ApplyTierBuff(staminaPenalty, speedPenalty, severity);
            _lastSeverity = severity;
            _lastAppliedStaminaPenalty = staminaPenalty;
            _lastAppliedSpeedPenalty = speedPenalty;

            _monitor.Log(
                $"[StressGameplay] severity={severity}, stamina=-{staminaPenalty}, speed=-{speedPenalty}, " +
                $"mode={_config.GameplayMode}, consolidate={_config.ConsolidateStressPenalties}",
                LogLevel.Trace);
        }

        public (int StaminaPenalty, int SpeedPenalty) ComputeCappedPenalties(StressSeverity severity)
        {
            var multiplier = _config.PenaltyStrengthMultiplier;
            if (multiplier <= 0f)
                return (0, 0);

            var tierStamina = GetTierStaminaPenalty(severity);
            var tierSpeed = GetTierSpeedPenalty(severity);

            int finalStamina;
            int finalSpeed;

            if (_config.ConsolidateStressPenalties)
            {
                finalStamina = ScaleAndCapStamina(tierStamina, multiplier);
                finalSpeed = ScaleAndCapSpeed(tierSpeed, multiplier);
            }
            else
            {
                var (causeStamina, causeSpeed) = GetMaxCauseBuffPenalties();
                finalStamina = ScaleAndCapStamina(Math.Max(tierStamina, causeStamina), multiplier);
                finalSpeed = ScaleAndCapSpeed(Math.Max(tierSpeed, causeSpeed), multiplier);
            }

            if (_data.ThunderFlashback.IsActive && severity >= StressSeverity.Critical)
            {
                // LightningFright: situational pressure only — no extra speed beyond Critical tier cap.
                finalSpeed = Math.Min(finalSpeed, ScaleAndCapSpeed(_config.CriticalSpeedPenalty, multiplier));
            }

            return (finalStamina, finalSpeed);
        }

        private int GetTierStaminaPenalty(StressSeverity severity) => severity switch
        {
            StressSeverity.Mild => _config.MildStaminaPenalty,
            StressSeverity.High => _config.HighStaminaPenalty,
            StressSeverity.Critical => _config.CriticalStaminaPenalty,
            _ => 0,
        };

        private int GetTierSpeedPenalty(StressSeverity severity)
        {
            if (severity < StressSeverity.Critical)
                return 0;

            return _config.CriticalSpeedPenalty;
        }

        private int ScaleAndCapStamina(int value, float multiplier)
        {
            if (value <= 0)
                return 0;

            return Math.Min(_config.MaxStaminaPenalty, (int)Math.Round(value * multiplier));
        }

        private int ScaleAndCapSpeed(int value, float multiplier)
        {
            if (value <= 0)
                return 0;

            return Math.Min(_config.MaxSpeedPenalty, (int)Math.Round(value * multiplier));
        }

        private (int Stamina, int Speed) GetMaxCauseBuffPenalties()
        {
            int maxStamina = 0;
            int maxSpeed = 0;

            foreach (var buffId in MechanicalStressBuffIds)
            {
                if (!_buffService.HasBuff(buffId))
                    continue;

                var (stam, speed) = _buffService.GetNegativeMechanicalPenalties(buffId);
                maxStamina = Math.Max(maxStamina, stam);
                maxSpeed = Math.Max(maxSpeed, speed);
            }

            return (maxStamina, maxSpeed);
        }

        private void ApplyCosmeticStressBuffs()
        {
            foreach (var buffId in MechanicalStressBuffIds)
            {
                if (!_buffService.HasBuff(buffId))
                    continue;

                // Уже косметический — не пересоздаём каждую секунду (мигание иконки, сброс тултипа).
                if (Game1.player.buffs.AppliedBuffs.TryGetValue(buffId, out var applied)
                    && !applied.effects.HasAnyValue())
                {
                    continue;
                }

                _buffService.ApplyCosmeticBuffFromData(buffId);
            }
        }

        /// <summary>
        /// Mild: только энергия. High: + дрожат руки (−1 рыбалка). Critical: + рассеянность (−1 удача).
        /// Ничего не блокирует — лишь делает день чуть тяжелее, пока стресс не снят.
        /// </summary>
        private void ApplyTierBuff(int staminaPenalty, int speedPenalty, StressSeverity severity)
        {
            int fishingPenalty = severity >= StressSeverity.High ? 1 : 0;
            int luckPenalty = severity >= StressSeverity.Critical ? 1 : 0;

            if (staminaPenalty <= 0 && speedPenalty <= 0 && fishingPenalty <= 0)
            {
                ClearTierBuff();
                return;
            }

            var effects = new BuffEffects();
            if (staminaPenalty > 0)
                effects.MaxStamina.Add(-staminaPenalty);
            if (speedPenalty > 0)
                effects.Speed.Add(-speedPenalty);
            if (fishingPenalty > 0)
                effects.FishingLevel.Add(-fishingPenalty);
            if (luckPenalty > 0)
                effects.LuckLevel.Add(-luckPenalty);

            _buffService.ApplyBuff(
                BuffIds.StressLoadTier,
                "Напряжение",
                effects,
                Buff.ENDLESS);
        }

        private void ClearTierBuff()
            => _buffService.RemoveBuff(BuffIds.StressLoadTier);

        private void SetClearMind(bool active)
        {
            bool has = _buffService.HasBuff(BuffIds.ClearMind);
            if (active == has)
                return;

            if (!active)
            {
                _buffService.RemoveBuff(BuffIds.ClearMind);
                return;
            }

            var effects = new BuffEffects();
            effects.LuckLevel.Add(1);
            _buffService.ApplyBuff(BuffIds.ClearMind, "Ясная голова", effects, Buff.ENDLESS);
        }

        /// <summary>Запомнить, когда и в каком состоянии игрок лёг спать.</summary>
        public void OnDayEnding()
        {
            _bedTime = Game1.timeOfDay;
            _severityAtBed = _stressLoadService.GetSeverity();
        }

        /// <summary>
        /// Сон влияет на стресс: лёг до 23:00 — стресс ниже; лёг после полуночи на «Критическом» — энергия не вся.
        /// </summary>
        public void OnDayStarted()
        {
            int bedTime = _bedTime;
            var severityAtBed = _severityAtBed;
            _bedTime = -1;

            if (bedTime < 0 || !_config.PenaltiesEnabled)
                return;

            if (bedTime < EarlyBedTime && _stressLoadService.GetCurrentStressLoad() > 0)
            {
                _stressLoadService.DecayStress(EarlySleepRelief);
                Game1.addHUDMessage(new HUDMessage("Ранний сон помог: тревога стала тише.", HUDMessage.newQuest_type));
                return;
            }

            if (bedTime >= 2400 && severityAtBed >= StressSeverity.Critical)
            {
                Game1.player.stamina = Math.Min(Game1.player.stamina, Game1.player.MaxStamina * RestlessSleepStaminaFactor);
                Game1.addHUDMessage(new HUDMessage("Беспокойный сон: стресс не дал толком отдохнуть.", HUDMessage.error_type));
            }
        }
    }
}
