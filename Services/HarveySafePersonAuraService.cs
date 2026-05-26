using System;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;
using StardewModdingAPI;
using StardewValley;

namespace HarveyStressMeter.Services
{
    /// <summary>
    /// «Рядом с Харви легче»: периодическое снижение StressLoad или бонус стабилизации Gotoro flashback.
    /// </summary>
    public sealed class HarveySafePersonAuraService
    {
        private static readonly string[] SafeAuraMessages =
        {
            "Рядом с Харви дышать немного легче.",
            "Его голос помогает удержаться здесь.",
            "Напряжение отпускает совсем чуть-чуть.",
        };

        private const float ClinicDistanceBonusTiles = 2f;
        private const float MarriedFarmHouseMaxDistanceTiles = 24f;
        private const float HarveyNearbyFlashbackStabilizationBonus = 1.15f;
        private const int ShelterSecondsPerDecayPoint = 4;

        private readonly SaveData _data;
        private readonly ModConfig _config;
        private readonly HarveyCareTrustService _trustService;
        private readonly StressLoadService _stressLoadService;
        private readonly ThunderFlashbackService _thunderFlashbackService;
        private readonly IMonitor _monitor;

        public HarveySafePersonAuraService(
            SaveData data,
            ModConfig config,
            HarveyCareTrustService trustService,
            StressLoadService stressLoadService,
            ThunderFlashbackService thunderFlashbackService,
            IMonitor monitor)
        {
            _data = data;
            _config = config;
            _trustService = trustService;
            _stressLoadService = stressLoadService;
            _thunderFlashbackService = thunderFlashbackService;
            _monitor = monitor;
        }

        public HarveySafePersonAuraState State => _data.HarveySafePersonAura;

        public HarveyProximityEvaluation EvaluateProximity()
        {
            if (!_config.EnableHarveySafePersonAura)
            {
                return new HarveyProximityEvaluation
                {
                    AuraEnabled = false,
                    BlockReason = "disabled",
                };
            }

            if (!TryGetHarveyDistanceTiles(out var distance, out var maxDistance, out var inSameLocation))
            {
                return new HarveyProximityEvaluation
                {
                    HarveyInSameLocation = inSameLocation,
                    DistanceTiles = distance,
                    EffectiveMaxDistanceTiles = maxDistance,
                };
            }

            var nearby = distance <= maxDistance;
            string? blockReason = null;
            if (!nearby)
                blockReason = "distance";
            else if (!IsSafeAuraEligible(out blockReason))
            {
                // blockReason set by eligibility check
            }

            var active = nearby && blockReason == null;

            return new HarveyProximityEvaluation
            {
                HarveyInSameLocation = true,
                DistanceTiles = distance,
                EffectiveMaxDistanceTiles = maxDistance,
                HarveyNearby = nearby,
                SafeAuraActive = active,
                BlockReason = blockReason,
            };
        }

        public bool IsHarveyWithinCareAuraRange()
            => EvaluateProximity().HarveyNearby;

        public float GetActiveFlashbackStabilizationMultiplier()
        {
            if (!_config.EnableHarveySafePersonAura)
                return 1f;

            var eval = EvaluateProximity();
            if (!eval.SafeAuraActive && !eval.HarveyNearby)
                return 1f;

            if (!_data.StressLoad.GotoroFlashbackActive || !_thunderFlashbackService.State.IsActive)
                return 1f;

            return HarveyNearbyFlashbackStabilizationBonus;
        }

        public void OnTimeChanged(int oldTime, int newTime)
        {
            if (!_config.EnableHarveySafePersonAura)
                return;

            if (!HasIntervalElapsed(oldTime, newTime, _config.SafeAuraDecayIntervalMinutes))
                return;

            ProcessSafeAuraTick(newTime);
        }

        public void ProcessSafeAuraTick(int currentTime = 0)
        {
            var eval = EvaluateProximity();
            State.LastHarveyNearby = eval.HarveyNearby;
            State.LastDistanceTiles = eval.DistanceTiles;
            State.LastSafeAuraActive = eval.SafeAuraActive;
            State.LastDecayAmount = 0;

            State.LastProcessTime = currentTime > 0 ? currentTime : Game1.timeOfDay;

            if (!eval.SafeAuraActive)
                return;

            var reduction = CalculateStressReduction();
            if (reduction <= 0)
                return;

            if (ShouldApplyFlashbackStabilizationInsteadOfDecay())
            {
                var shelterBonus = reduction * ShelterSecondsPerDecayPoint;
                _thunderFlashbackService.ApplySafeAuraShelterBonus(shelterBonus);
                State.LastDecayAmount = 0;
                _monitor.Log(
                    $"[SafeAura] Gotoro flashback stabilization +{shelterBonus}s shelter (Harvey nearby)",
                    LogLevel.Trace);
            }
            else if (_stressLoadService.GetCurrentStressLoad() > 0)
            {
                _stressLoadService.DecayStress(reduction);
                State.LastDecayAmount = reduction;
                _monitor.Log(
                    $"[SafeAura] StressLoad -{reduction} (trust L{_trustService.GetTrustLevel()}, dist {eval.DistanceTiles:F1})",
                    LogLevel.Trace);
            }

            TryShowAuraMessage();
        }

        public string BuildDebugSnapshot()
        {
            var eval = EvaluateProximity();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== HarveySafePersonAura ===");
            sb.AppendLine($"Enabled: {_config.EnableHarveySafePersonAura}");
            sb.AppendLine($"Harvey nearby: {eval.HarveyNearby}");
            sb.AppendLine($"Safe aura active: {eval.SafeAuraActive}");
            sb.AppendLine($"Distance to Harvey: {(eval.DistanceTiles >= 0 ? $"{eval.DistanceTiles:F1} tiles" : "(n/a)")}");
            sb.AppendLine($"Max distance: {eval.EffectiveMaxDistanceTiles:F1} tiles");
            sb.AppendLine($"Stress decay from aura last tick: {State.LastDecayAmount}");
            sb.AppendLine($"Last process time: {State.LastProcessTime}");
            if (!string.IsNullOrEmpty(eval.BlockReason))
                sb.AppendLine($"Block reason: {eval.BlockReason}");
            return sb.ToString().TrimEnd();
        }

        private bool IsSafeAuraEligible(out string? blockReason)
        {
            blockReason = null;

            if (_trustService.GetTrustLevel() < HarveyCareTrustLevels.SafePerson)
            {
                blockReason = "trust<3";
                return false;
            }

            if (HarveyFriendshipHelper.GetHarveyHearts() < 6)
            {
                blockReason = "hearts<6";
                return false;
            }

            if (GameStateHelper.IsSafeAuraBlockedContext())
            {
                blockReason = "blocked_context";
                return false;
            }

            return true;
        }

        private bool TryGetHarveyDistanceTiles(
            out float distanceTiles,
            out float maxDistanceTiles,
            out bool inSameLocation)
        {
            distanceTiles = -1f;
            maxDistanceTiles = Math.Max(1f, _config.HarveySafeDistanceTiles);
            inSameLocation = false;

            if (!GameStateHelper.TryGetHarveyDistanceTiles(out _, out distanceTiles))
                return false;

            inSameLocation = true;
            maxDistanceTiles = GetEffectiveMaxDistanceTiles();
            return true;
        }

        private float GetEffectiveMaxDistanceTiles()
        {
            var baseDistance = Math.Max(1f, _config.HarveySafeDistanceTiles);

            if (GameStateHelper.IsMarriedFarmHouseContext())
                return MarriedFarmHouseMaxDistanceTiles;

            if (GameStateHelper.IsClinicLocation())
                return baseDistance + ClinicDistanceBonusTiles;

            return baseDistance;
        }

        private int CalculateStressReduction()
        {
            var level = _trustService.GetTrustLevel();
            var amount = level >= HarveyCareTrustLevels.Anchor
                ? _config.SafeAuraStressReductionLevel4
                : _config.SafeAuraStressReductionLevel3;

            if (HarveyFriendshipHelper.IsDatingHarvey())
                amount += _config.DatingSafeAuraBonus;

            if (HarveyFriendshipHelper.IsMarriedToHarvey())
                amount += _config.MarriedSafeAuraBonus;

            return Math.Clamp(amount, 0, 4);
        }

        private bool ShouldApplyFlashbackStabilizationInsteadOfDecay()
        {
            return _data.StressLoad.GotoroFlashbackActive
                   && _thunderFlashbackService.State.IsActive;
        }

        private void TryShowAuraMessage()
        {
            if (!_config.ShowSafeAuraMessages || !_config.EnableHudMessages)
                return;

            if (!CanShowAuraMessage())
                return;

            var text = SafeAuraMessages[Game1.random.Next(SafeAuraMessages.Length)];
            Game1.addHUDMessage(new HUDMessage(text, HUDMessage.achievement_type));
            State.LastMessageTime = Game1.timeOfDay;

            _monitor.Log($"[SafeAura] HUD: {text}", LogLevel.Trace);
        }

        private bool CanShowAuraMessage()
        {
            if (State.LastMessageTime <= 0)
                return true;

            var cooldown = Math.Max(1, _config.SafeAuraMessageCooldownMinutes) * 10;
            return Math.Abs(Game1.timeOfDay - State.LastMessageTime) >= cooldown;
        }

        private static bool HasIntervalElapsed(int oldTime, int newTime, int intervalMinutes)
        {
            if (intervalMinutes <= 0)
                return true;

            var interval = intervalMinutes * 10;
            if (oldTime <= 0)
                return newTime % interval == 0;

            return Math.Abs(newTime - oldTime) >= interval || newTime % interval == 0;
        }
    }
}
