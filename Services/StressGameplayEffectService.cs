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
                return;
            }

            var severity = _stressLoadService.GetSeverity();

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

            ApplyTierBuff(staminaPenalty, speedPenalty);
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

                _buffService.ApplyCosmeticBuffFromData(buffId);
            }
        }

        private void ApplyTierBuff(int staminaPenalty, int speedPenalty)
        {
            if (staminaPenalty <= 0 && speedPenalty <= 0)
            {
                ClearTierBuff();
                return;
            }

            var effects = new BuffEffects();
            if (staminaPenalty > 0)
                effects.MaxStamina.Add(-staminaPenalty);
            if (speedPenalty > 0)
                effects.Speed.Add(-speedPenalty);

            _buffService.ApplyBuff(
                BuffIds.StressLoadTier,
                "Напряжение",
                effects,
                Buff.ENDLESS);
        }

        private void ClearTierBuff()
            => _buffService.RemoveBuff(BuffIds.StressLoadTier);
    }
}
