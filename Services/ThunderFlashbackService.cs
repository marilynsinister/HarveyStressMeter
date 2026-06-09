using System;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;
using StardewModdingAPI;
using StardewValley;

namespace HarveyStressMeter.Services
{
    /// <summary>
    /// Атмосферный испуг молнии во время грозы (Gotoro war trauma).
    /// Не телепортирует и не отбирает управление — мягко подталкивает к лесному укрытию.
    /// После помощи Харви — стабилизация эпизода с grace period и возможным лёгким relapse.
    /// </summary>
    public sealed class ThunderFlashbackService
    {
        private const int FrightCheckIntervalMinutes = 100;
        private const int RelapseCheckIntervalMinutes = 100;

        private readonly SaveData _data;
        private readonly StressLoadService _stressLoadService;
        private readonly TreatmentService _treatmentService;
        private readonly BuffService _buffService;
        private readonly StateService _stateService;
        private readonly LightningFrightMessageService _messageService;
        private readonly ModConfig _config;
        private readonly IMonitor _monitor;
        private EpisodeQuestProgressService? _episodeQuestProgressService;
        private HarveyCareTrustService? _trustService;
        private HarveySafePersonAuraService? _safeAuraService;
        private StressSystemsCoordinator? _coordinator;

        private string? _lastLocationName;
        private bool _relapseWarningShownToday;
        private int _lastRelapseCheckTime;

        public ThunderFlashbackService(
            SaveData data,
            StressLoadService stressLoadService,
            TreatmentService treatmentService,
            BuffService buffService,
            StateService stateService,
            LightningFrightMessageService messageService,
            ModConfig config,
            IMonitor monitor)
        {
            _data = data;
            _stressLoadService = stressLoadService;
            _treatmentService = treatmentService;
            _buffService = buffService;
            _stateService = stateService;
            _messageService = messageService;
            _config = config;
            _monitor = monitor;
        }

        public ThunderFlashbackState State => _data.ThunderFlashback;

        public void SetTrustService(HarveyCareTrustService trustService)
            => _trustService = trustService;

        public void SetSafeAuraService(HarveySafePersonAuraService safeAuraService)
            => _safeAuraService = safeAuraService;

        public void SetCoordinator(StressSystemsCoordinator coordinator)
            => _coordinator = coordinator;

        public void SetEpisodeQuestProgressService(EpisodeQuestProgressService episodeQuestProgressService)
            => _episodeQuestProgressService = episodeQuestProgressService;

        public void ResetDailyState()
        {
            State.WasTriggeredToday = false;
            State.WasStabilizedToday = false;
            State.WasPrimaryFlashbackTriggeredToday = false;
            State.WasRelapseTriggeredToday = false;
            State.IsStabilizedByHarveyToday = false;
            State.StabilizedByHarveyAtTime = 0;
            State.HarveyAnchorGraceUntil = 0;
            State.ThunderRelapseCooldownUntil = 0;
            State.LeftHarveyAnchorAfterStabilization = false;
            State.StabilizedByHarveyLocation = null;
            State.LastRelapseTime = 0;
            State.IsActive = false;
            State.EnteredForestDuringFlashback = false;
            State.ForestShelterSeconds = 0;
            State.TriggerLocation = null;
            State.TriggerTime = 0;
            State.LightningFrightIntensity = 0;
            State.IsGotoroFlashback = false;
            State.LastHudMessageTime = 0;
            State.HudMessageCooldownMinutes = _messageService.RollCooldownMinutes();
            _lastLocationName = null;
            _relapseWarningShownToday = false;
            _lastRelapseCheckTime = 0;

            if (State.ThunderSensitivityDays > 0)
            {
                State.ThunderSensitivityDays--;
                if (State.ThunderSensitivityDays <= 0)
                    _stressLoadService.RemoveCause(StressCauses.ThunderSensitivity);
            }

            _stressLoadService.RemoveCause(StressCauses.ThunderRelapse);
        }

        public void ResetFlashbackState()
        {
            State.IsActive = false;
            State.WasTriggeredToday = false;
            State.WasStabilizedToday = false;
            State.WasPrimaryFlashbackTriggeredToday = false;
            State.WasRelapseTriggeredToday = false;
            State.IsStabilizedByHarveyToday = false;
            State.StabilizedByHarveyAtTime = 0;
            State.HarveyAnchorGraceUntil = 0;
            State.ThunderRelapseCooldownUntil = 0;
            State.LeftHarveyAnchorAfterStabilization = false;
            State.ThunderSensitivityDays = 0;
            State.LastRelapseTime = 0;
            State.StabilizedByHarveyLocation = null;
            State.EnteredForestDuringFlashback = false;
            State.ForestShelterSeconds = 0;
            State.RequiredForestShelterSeconds = _config.ForestShelterRequiredSeconds;
            State.TriggerLocation = null;
            State.TriggerTime = 0;
            State.LastFrightCheckTime = 0;
            State.LastHudMessageTime = 0;
            State.HudMessageCooldownMinutes = _messageService.RollCooldownMinutes();
            State.LightningFrightIntensity = 0;
            State.IsGotoroFlashback = false;
            _stressLoadService.SetGotoroFlashbackActive(false);
            _stressLoadService.RemoveCause(StressCauses.GotoroFlashback);
            _stressLoadService.RemoveCause(StressCauses.ThunderRelapse);
            _stressLoadService.RemoveCause(StressCauses.ThunderSensitivity);
            _lastLocationName = null;
            _relapseWarningShownToday = false;
            _lastRelapseCheckTime = 0;
        }

        public void OnTimeChanged(int oldTime, int newTime)
        {
            if (GameStateHelper.IsStormWeather())
            {
                if (CanEvaluateFrightRoll())
                {
                    if (Math.Abs(newTime - State.LastFrightCheckTime) >= FrightCheckIntervalMinutes
                        || newTime % FrightCheckIntervalMinutes == 0)
                    {
                        State.LastFrightCheckTime = newTime;
                        TryRollFright(force: false);
                    }
                }

                UpdateRelapseMonitoring(newTime);
            }
        }

        public void OnLocationChanged(string? previousLocation, string? newLocation)
        {
            if (State.IsActive)
            {
                if (GameStateHelper.IsForestShelterLocation())
                {
                    if (!State.EnteredForestDuringFlashback)
                    {
                        State.EnteredForestDuringFlashback = true;
                        _messageService.TryShowPhaseMessage(
                            State,
                            LightningFrightMessagePhase.MovingToForest,
                            force: true);
                    }
                }
                else if (GameStateHelper.IsTownLocation()
                         && IsForestLocationName(previousLocation))
                {
                    _messageService.TryShowPhaseMessage(
                        State,
                        LightningFrightMessagePhase.ReturnedTooEarly);
                }
            }
            else if (State.IsStabilizedByHarveyToday
                     && WasInHarveyAnchorZone(previousLocation)
                     && !IsInHarveyAnchorZone(newLocation))
            {
                State.LeftHarveyAnchorAfterStabilization = true;
                _messageService.TryShowPhaseMessage(
                    State,
                    LightningFrightMessagePhase.LeavingHarveyAnchor,
                    force: true);
            }

            _lastLocationName = newLocation;

            if (GameStateHelper.IsStormWeather())
                UpdateRelapseMonitoring(Game1.timeOfDay, forceCheck: true);
        }

        public void UpdateRelapseMonitoring(int currentTime, bool forceCheck = false)
        {
            if (!GameStateHelper.IsStormWeather())
                return;

            if (!forceCheck
                && Math.Abs(currentTime - _lastRelapseCheckTime) < RelapseCheckIntervalMinutes
                && currentTime % RelapseCheckIntervalMinutes != 0)
            {
                return;
            }

            _lastRelapseCheckTime = currentTime;

            if (IsInHarveyAnchorZone())
            {
                if (State.IsStabilizedByHarveyToday
                    && _safeAuraService?.EvaluateProximity().SafeAuraActive == true
                    && CalculateRelapseChancePercent() <= 0)
                {
                    _messageService.TryShowPhaseMessage(
                        State,
                        LightningFrightMessagePhase.RelapseSuppressedNearHarvey);
                }

                return;
            }

            TryRollThunderRelapse(ignoreChance: false);
        }

        private static bool IsForestLocationName(string? locationName) =>
            !string.IsNullOrEmpty(locationName)
            && (locationName.Equals("Forest", StringComparison.OrdinalIgnoreCase)
                || locationName.Equals("Woods", StringComparison.OrdinalIgnoreCase)
                || locationName.Equals("SecretWoods", StringComparison.OrdinalIgnoreCase));

        public void UpdateActiveFlashback(int elapsedSeconds = 1)
        {
            if (!State.IsActive || elapsedSeconds <= 0)
                return;

            if (GameStateHelper.IsBlockingFlashbackContext())
                return;

            if (GameStateHelper.IsForestShelterLocation())
            {
                if (!State.EnteredForestDuringFlashback)
                    State.EnteredForestDuringFlashback = true;

                var shelterDelta = elapsedSeconds;
                var stabilizationMult = _trustService?.GetFlashbackStabilizationMultiplier() ?? 1f;
                stabilizationMult *= _safeAuraService?.GetActiveFlashbackStabilizationMultiplier() ?? 1f;
                shelterDelta = (int)MathF.Round(elapsedSeconds * stabilizationMult);

                State.ForestShelterSeconds += Math.Max(1, shelterDelta);
                UpdateActiveTreatmentProgress();

                if (State.ForestShelterSeconds >= State.RequiredForestShelterSeconds)
                {
                    StabilizeFlashback();
                    return;
                }

                _messageService.TryShowPhaseMessage(State, LightningFrightMessagePhase.InForest);
                return;
            }

            if (GameStateHelper.IsTownLocation())
            {
                _messageService.TryShowPhaseMessage(State, LightningFrightMessagePhase.MovingToForest);
            }
        }

        public bool TryRollFright(bool force = false)
        {
            if (!force && !CanEvaluateFrightRoll())
                return false;

            if (!force && State.WasPrimaryFlashbackTriggeredToday)
                return false;

            if (!force && State.IsActive)
                return false;

            if (!force && GameStateHelper.IsForestShelterLocation())
                return false;

            if (!force)
            {
                var chance = CalculateFrightChancePercent();
                if (Game1.random.Next(100) >= chance)
                    return false;
            }

            TriggerFlashback(force);
            return true;
        }

        public void TriggerFlashback(bool force = false, bool relapse = false)
        {
            if (!force && !CanEvaluateFrightRoll())
                return;

            if (relapse && State.WasPrimaryFlashbackTriggeredToday)
                return;

            var intensity = CalculateIntensity(relapse);
            var load = _stressLoadService.GetCurrentStressLoad();
            var isGotoro = relapse
                ? intensity >= 70
                  || _data.StressLoad.WarTraumaFlag
                  || load >= 75
                : intensity >= 70
                  || _data.StressLoad.WarTraumaFlag
                  || load >= 75;

            State.IsActive = true;
            State.WasTriggeredToday = true;
            State.WasPrimaryFlashbackTriggeredToday = true;
            State.WasStabilizedToday = false;
            State.TriggerLocation = Game1.currentLocation?.NameOrUniqueName;
            State.TriggerTime = Game1.timeOfDay;
            State.LightningFrightIntensity = intensity;
            State.IsGotoroFlashback = isGotoro;
            State.EnteredForestDuringFlashback = GameStateHelper.IsForestShelterLocation();
            State.ForestShelterSeconds = 0;
            State.RequiredForestShelterSeconds = isGotoro
                ? _config.ForestShelterRequiredSeconds + _config.GotoroForestShelterBonusSeconds
                : _config.ForestShelterRequiredSeconds;
            State.HudMessageCooldownMinutes = _messageService.RollCooldownMinutes();

            _stressLoadService.RemoveCause(StressCauses.ThunderRelapse);
            _stressLoadService.AddCause(StressCauses.Thunder, BuffIds.Thunder);

            if (isGotoro)
            {
                _data.StressLoad.WarTraumaFlag = true;
                _stressLoadService.SetGotoroFlashbackActive(true);
                SyncGotoroFlashbackTopic(add: true);
                _coordinator?.OnGotoroFlashbackTriggered(isGotoro: true);
            }

            _stressLoadService.Recalculate();

            _messageService.TryShowPhaseMessage(
                State,
                LightningFrightMessagePhase.Triggered,
                force: true);

            _monitor.Log(
                $"[ThunderFlashback] Triggered intensity={intensity}, gotoro={isGotoro}, relapse={relapse}, " +
                $"load={load}, episode={_stressLoadService.GetCandidateEpisode() ?? "(none)"}",
                LogLevel.Info);
        }

        /// <summary>
        /// Харви стабилизировал текущий эпизод: снимает активный flashback, даёт grace period,
        /// частично снижает StressLoad, сохраняет скрытую чувствительность к грозе.
        /// </summary>
        public void StabilizeWithHarvey(int stressDecay, int graceMinutes, string reason)
        {
            var wasGotoro = State.IsGotoroFlashback;

            if (State.IsActive)
            {
                State.IsActive = false;
                if (wasGotoro)
                    SyncGotoroFlashbackTopic(add: false);
            }

            var now = Game1.timeOfDay;
            State.IsStabilizedByHarveyToday = true;
            State.StabilizedByHarveyAtTime = now;
            State.StabilizedByHarveyLocation = Game1.currentLocation?.NameOrUniqueName;
            State.HarveyAnchorGraceUntil = AddGameMinutes(now, graceMinutes);
            State.ThunderRelapseCooldownUntil = AddGameMinutes(
                now,
                Math.Max(1, _config.ThunderRelapseCooldownMinutes));
            State.LeftHarveyAnchorAfterStabilization = false;
            State.WasStabilizedToday = true;
            _relapseWarningShownToday = false;

            if (stressDecay > 0)
                _stressLoadService.DecayStress(stressDecay);

            if (_stressLoadService.GetActiveCauses().ContainsKey(StressCauses.Thunder))
            {
                _stressLoadService.RemoveCause(StressCauses.Thunder);
                if (_buffService.HasBuff(BuffIds.Thunder))
                    _buffService.RemoveBuff(BuffIds.Thunder);
            }

            _stressLoadService.RemoveCause(StressCauses.ThunderRelapse);

            if (State.ThunderSensitivityDays <= 0)
            {
                State.ThunderSensitivityDays = Math.Max(
                    1,
                    _config.ThunderSensitivityDaysAfterHarveyCare);
            }

            if (!_stressLoadService.GetActiveCauses().ContainsKey(StressCauses.ThunderSensitivity))
            {
                _stressLoadService.AddCause(
                    StressCauses.ThunderSensitivity,
                    weightOverride: StressCauses.GetBaseWeight(StressCauses.ThunderSensitivity));
            }

            _stressLoadService.Recalculate();
            _trustService?.OnSafeLocationStabilized(harveyHelped: true);

            _messageService.TryShowPhaseMessage(
                State,
                LightningFrightMessagePhase.AfterHarveyStabilized,
                force: true);

            _monitor.Log(
                $"[ThunderFlashback] StabilizeWithHarvey reason={reason}, decay={stressDecay}, " +
                $"graceUntil={State.HarveyAnchorGraceUntil}, sensitivityDays={State.ThunderSensitivityDays}, " +
                $"load={_stressLoadService.GetCurrentStressLoad()}",
                LogLevel.Info);
        }

        public bool TryRollThunderRelapse(bool ignoreChance = false)
        {
            if (!CanEvaluateRelapseRoll())
                return false;

            if (!_relapseWarningShownToday && !State.WasRelapseTriggeredToday)
            {
                _relapseWarningShownToday = true;
                State.LastRelapseTime = Game1.timeOfDay;
                _messageService.TryShowPhaseMessage(
                    State,
                    LightningFrightMessagePhase.RelapseWarning,
                    force: true);
                State.ThunderRelapseCooldownUntil = AddGameMinutes(
                    Game1.timeOfDay,
                    Math.Max(10, _config.ThunderRelapseCooldownMinutes / 2));
                return false;
            }

            var chance = CalculateRelapseChancePercent();
            if (!ignoreChance && chance > 0 && Game1.random.Next(100) >= chance)
                return false;

            if (ShouldTriggerHeavyRelapse())
            {
                if (State.WasPrimaryFlashbackTriggeredToday)
                    return false;

                TriggerFlashback(force: false, relapse: true);
                return true;
            }

            if (State.WasRelapseTriggeredToday)
                return false;

            ApplyLightRelapse();
            return true;
        }

        public int CalculateRelapseChancePercent()
        {
            if (State.IsActive)
                return 0;

            if (!State.IsStabilizedByHarveyToday
                && State.ThunderSensitivityDays <= 0
                && !_stressLoadService.GetActiveCauses().ContainsKey(StressCauses.ThunderSensitivity))
            {
                return 0;
            }

            if (IsInHarveyAnchorZone())
                return 0;

            if (IsGracePeriodActive())
                return 0;

            if (Game1.timeOfDay < State.ThunderRelapseCooldownUntil)
                return 0;

            if (_safeAuraService?.EvaluateProximity().SafeAuraActive == true
                && State.IsStabilizedByHarveyToday)
            {
                return 0;
            }

            double chance = 10;

            if (IsOpenThunderLocation())
                chance += 10;

            if (Game1.timeOfDay >= 2000)
                chance += 10;

            var load = _stressLoadService.GetCurrentStressLoad();
            if (load >= 75)
                chance += 20;
            else if (load >= 50)
                chance += 15;

            var causes = _stressLoadService.GetActiveCauses();
            if (causes.ContainsKey(StressCauses.NoSleep))
                chance += 10;
            if (causes.ContainsKey(StressCauses.Hunger))
                chance += 5;
            if (causes.ContainsKey(StressCauses.TooCold))
                chance += 5;

            if (GameStateHelper.IsDangerousStressLocation()
                || GameStateHelper.HasHostileMonstersNearby())
            {
                chance += 15;
            }

            if (State.WasPrimaryFlashbackTriggeredToday || State.WasTriggeredToday)
                chance += 20;

            if (GameStateHelper.IsPlayerHomeLocation())
                chance -= 20;

            if (GameStateHelper.IsClinicLocation())
                chance -= 20;

            if (GameStateHelper.IsForestShelterLocation())
                chance -= 30;

            return (int)Math.Clamp(chance, 0, 65);
        }

        public void MarkHarveyHelped(int forestShelterBonusSeconds)
        {
            if (!State.IsActive || forestShelterBonusSeconds <= 0)
                return;

            State.ForestShelterSeconds += forestShelterBonusSeconds;
            _monitor.Log(
                $"[ThunderFlashback] Harvey helped: +{forestShelterBonusSeconds}s shelter " +
                $"({State.ForestShelterSeconds}/{State.RequiredForestShelterSeconds})",
                LogLevel.Debug);
        }

        public void ApplySafeAuraShelterBonus(int bonusSeconds)
        {
            if (!State.IsActive || bonusSeconds <= 0)
                return;

            State.ForestShelterSeconds += bonusSeconds;
            UpdateActiveTreatmentProgress();

            if (State.ForestShelterSeconds >= State.RequiredForestShelterSeconds)
                StabilizeFlashback();
        }

        public void StabilizeFlashback(bool applyStressDecay = true)
        {
            if (!State.IsActive)
                return;

            State.IsActive = false;
            State.WasStabilizedToday = true;

            if (State.IsGotoroFlashback)
                SyncGotoroFlashbackTopic(add: false);

            var stabilizedText = _messageService.PickMessage(LightningFrightMessagePhase.Stabilized)
                ?? "Звук всё ещё здесь, но он снова принадлежит небу.";

            FlashbackStabilizationOutcome? outcome = null;
            if (_coordinator != null)
            {
                outcome = _coordinator.OnFlashbackStabilized(
                    State.IsGotoroFlashback,
                    State.ForestShelterSeconds,
                    applyStressDecay
                        ? amount => _stressLoadService.DecayStress(amount)
                        : null);
            }
            else if (applyStressDecay)
            {
                _stressLoadService.DecayStress(State.IsGotoroFlashback ? 20 : 12);
            }

            if (outcome?.DeferredEpisodeStart == true && State.IsGotoroFlashback)
            {
                State.DeferredGotoroShelterSeconds = Math.Max(
                    State.DeferredGotoroShelterSeconds,
                    State.ForestShelterSeconds);
            }

            _messageService.ShowMessage(State, stabilizedText, force: true);

            if (GameStateHelper.IsForestShelterLocation())
                _trustService?.OnSafeLocationStabilized(harveyHelped: false);

            if (outcome?.MarkedReadyForReview == true
                && !string.IsNullOrEmpty(outcome.ReadyForReviewEpisodeId))
            {
                _treatmentService.MarkTreatmentReadyForReviewByEpisode(
                    outcome.ReadyForReviewEpisodeId,
                    stabilizedText);
            }

            _monitor.Log(
                $"[ThunderFlashback] Stabilized after {State.ForestShelterSeconds}s in forest, " +
                $"episode={_stressLoadService.GetCandidateEpisode() ?? "(none)"}",
                LogLevel.Info);
        }

        public void ResolveThunderRelapseCauses(bool includeSensitivity = false)
        {
            _stressLoadService.RemoveCause(StressCauses.ThunderRelapse);
            if (includeSensitivity)
                _stressLoadService.RemoveCause(StressCauses.ThunderSensitivity);
        }

        public string BuildDebugSnapshot()
        {
            return $"""
                === Thunder Flashback ===
                IsActive: {State.IsActive}
                WasTriggeredToday: {State.WasTriggeredToday}
                WasPrimaryFlashbackToday: {State.WasPrimaryFlashbackTriggeredToday}
                WasRelapseTriggeredToday: {State.WasRelapseTriggeredToday}
                WasStabilizedToday: {State.WasStabilizedToday}
                IsStabilizedByHarveyToday: {State.IsStabilizedByHarveyToday}
                StabilizedByHarveyAt: {State.StabilizedByHarveyAtTime} @ {State.StabilizedByHarveyLocation ?? "(none)"}
                HarveyAnchorGraceUntil: {State.HarveyAnchorGraceUntil}
                ThunderRelapseCooldownUntil: {State.ThunderRelapseCooldownUntil}
                LeftHarveyAnchor: {State.LeftHarveyAnchorAfterStabilization}
                ThunderSensitivityDays: {State.ThunderSensitivityDays}
                LastRelapseTime: {State.LastRelapseTime}
                IsGotoroFlashback: {State.IsGotoroFlashback}
                Intensity: {State.LightningFrightIntensity}
                Trigger: {State.TriggerLocation ?? "(none)"} @ {State.TriggerTime}
                ForestShelter: {State.ForestShelterSeconds}/{State.RequiredForestShelterSeconds}
                EnteredForest: {State.EnteredForestDuringFlashback}
                LastHudMessage: {State.LastHudMessageTime}, cooldown={State.HudMessageCooldownMinutes}m
                Fright chance now: {CalculateFrightChancePercent()}%
                Relapse chance now: {CalculateRelapseChancePercent()}%
                Grace active: {IsGracePeriodActive()}
                In anchor zone: {IsInHarveyAnchorZone()}
                WarTraumaFlag: {_data.StressLoad.WarTraumaFlag}
                """;
        }

        public string BuildMcpSnapshot()
        {
            var aura = _safeAuraService?.EvaluateProximity();
            return $"""
                ok: true
                isActive: {State.IsActive}
                isStabilizedByHarveyToday: {State.IsStabilizedByHarveyToday}
                harveyAnchorGraceUntil: {State.HarveyAnchorGraceUntil}
                thunderRelapseCooldownUntil: {State.ThunderRelapseCooldownUntil}
                leftHarveyAnchor: {State.LeftHarveyAnchorAfterStabilization}
                relapseChanceNow: {CalculateRelapseChancePercent()}
                frightChanceNow: {CalculateFrightChancePercent()}
                graceActive: {IsGracePeriodActive()}
                inAnchorZone: {IsInHarveyAnchorZone()}
                safeAuraActive: {aura?.SafeAuraActive ?? false}
                wasPrimaryFlashbackToday: {State.WasPrimaryFlashbackTriggeredToday}
                wasRelapseTriggeredToday: {State.WasRelapseTriggeredToday}
                thunderSensitivityDays: {State.ThunderSensitivityDays}
                stressLoad: {_stressLoadService.GetCurrentStressLoad()}
                activeCauses: {string.Join(", ", _stressLoadService.GetActiveCauses().Keys)}
                """;
        }

        private bool CanEvaluateRelapseRoll()
        {
            if (!Context.IsWorldReady)
                return false;

            if (!GameStateHelper.IsStormWeather())
                return false;

            if (State.IsActive)
                return false;

            if (GameStateHelper.IsIndoors())
                return false;

            if (GameStateHelper.IsBlockingFlashbackContext())
                return false;

            if (!State.IsStabilizedByHarveyToday
                && State.ThunderSensitivityDays <= 0
                && !_stressLoadService.GetActiveCauses().ContainsKey(StressCauses.ThunderSensitivity))
            {
                return false;
            }

            if (State.WasRelapseTriggeredToday && State.WasPrimaryFlashbackTriggeredToday)
                return false;

            if (IsGracePeriodActive())
                return false;

            if (Game1.timeOfDay < State.ThunderRelapseCooldownUntil)
                return false;

            if (IsInHarveyAnchorZone())
                return false;

            return true;
        }

        private bool ShouldTriggerHeavyRelapse()
        {
            if (State.WasPrimaryFlashbackTriggeredToday)
                return false;

            var load = _stressLoadService.GetCurrentStressLoad();
            var night = Game1.timeOfDay >= 2000;
            var farFromHarvey = _safeAuraService?.EvaluateProximity().SafeAuraActive != true;

            if (!farFromHarvey)
                return false;

            if (_data.StressLoad.WarTraumaFlag && load >= 50 && night)
                return true;

            return load >= 75 && night;
        }

        private void ApplyLightRelapse()
        {
            State.WasRelapseTriggeredToday = true;
            State.LastRelapseTime = Game1.timeOfDay;
            State.ThunderRelapseCooldownUntil = AddGameMinutes(
                Game1.timeOfDay,
                Math.Max(1, _config.ThunderRelapseCooldownMinutes));

            _stressLoadService.AddCause(
                StressCauses.ThunderRelapse,
                weightOverride: StressCauses.GetBaseWeight(StressCauses.ThunderRelapse));

            _stressLoadService.Recalculate();

            _messageService.TryShowPhaseMessage(
                State,
                LightningFrightMessagePhase.RelapseTriggered,
                force: true);

            _monitor.Log(
                $"[ThunderFlashback] Light relapse applied, load={_stressLoadService.GetCurrentStressLoad()}, " +
                $"episode candidate={_stressLoadService.GetCandidateEpisode() ?? "(none)"}",
                LogLevel.Info);
        }

        private bool IsGracePeriodActive()
            => State.IsStabilizedByHarveyToday
               && State.HarveyAnchorGraceUntil > 0
               && Game1.timeOfDay < State.HarveyAnchorGraceUntil;

        private bool IsInHarveyAnchorZone(string? locationName = null)
        {
            if (IsGracePeriodActive() && IsPhysicalAnchorLocation(locationName))
                return true;

            if (State.IsStabilizedByHarveyToday
                && _safeAuraService?.EvaluateProximity().SafeAuraActive == true)
            {
                return true;
            }

            return IsPhysicalAnchorLocation(locationName);
        }

        private bool WasInHarveyAnchorZone(string? locationName)
            => IsPhysicalAnchorLocation(locationName);

        private static bool IsPhysicalAnchorLocation(string? locationName = null)
        {
            if (!string.IsNullOrEmpty(locationName))
            {
                return locationName is "Hospital" or "FarmHouse" or "IslandFarmHouse" or "Cabin"
                    or "Forest" or "Woods" or "SecretWoods";
            }

            return GameStateHelper.IsClinicLocation()
                   || GameStateHelper.IsPlayerHomeLocation()
                   || GameStateHelper.IsForestShelterLocation();
        }

        private static bool IsOpenThunderLocation()
        {
            var name = Game1.currentLocation?.NameOrUniqueName ?? "";
            return name is "Town" or "Mountain" or "Beach" or "BusStop" or "Railroad";
        }

        private static int AddGameMinutes(int timeOfDay, int minutes)
            => Math.Min(2600, timeOfDay + Math.Max(0, minutes) * 10);

        private bool CanEvaluateFrightRoll()
        {
            if (!Context.IsWorldReady)
                return false;

            if (!GameStateHelper.IsStormWeather())
                return false;

            if (GameStateHelper.IsIndoors())
                return false;

            if (GameStateHelper.IsBlockingFlashbackContext())
                return false;

            return true;
        }

        private int CalculateFrightChancePercent()
        {
            double chance = _config.LightningFrightBaseChance;

            if (GameStateHelper.IsStormWeather())
                chance += 15;

            if (_buffService.HasBuff(BuffIds.Thunder) || _stateService.HasBuffInGame(BuffIds.Thunder))
                chance += 20;

            var load = _stressLoadService.GetCurrentStressLoad();
            if (load >= 75)
                chance += 20;
            else if (load >= 50)
                chance += 10;

            if (GameStateHelper.IsTownLocation())
                chance += 10;

            if (_stressLoadService.GetActiveCauses().ContainsKey(StressCauses.NoSleep))
                chance += 10;

            if (_stressLoadService.GetActiveCauses().ContainsKey(StressCauses.Hunger))
                chance += 5;

            if (_stressLoadService.GetActiveCauses().ContainsKey(StressCauses.TooCold))
                chance += 5;

            if (State.ThunderSensitivityDays > 0
                || _stressLoadService.GetActiveCauses().ContainsKey(StressCauses.ThunderSensitivity))
            {
                chance += 5;
            }

            return (int)Math.Clamp(chance, 0, 75);
        }

        private int CalculateIntensity(bool relapse = false)
        {
            int intensity = relapse ? 30 : 35;

            var load = _stressLoadService.GetCurrentStressLoad();
            if (load >= 75)
                intensity += relapse ? 25 : 30;
            else if (load >= 50)
                intensity += relapse ? 10 : 15;

            if (_buffService.HasBuff(BuffIds.Thunder))
                intensity += relapse ? 10 : 15;

            if (GameStateHelper.IsTownLocation())
                intensity += 10;

            if (_data.StressLoad.WarTraumaFlag)
                intensity += 15;

            return Math.Clamp(intensity, 0, 100);
        }

        private static void SyncGotoroFlashbackTopic(bool add)
        {
            if (add)
            {
                if (!ConversationHelper.HasTopic(TopicIds.GotoroFlashbackActive))
                    ConversationHelper.AddTopic(TopicIds.GotoroFlashbackActive, 0);
                return;
            }

            if (ConversationHelper.HasTopic(TopicIds.GotoroFlashbackActive))
                ConversationHelper.RemoveTopic(TopicIds.GotoroFlashbackActive);

            if (ConversationHelper.HasTopic(TopicIds.GotoroForestRescuePending))
                ConversationHelper.RemoveTopic(TopicIds.GotoroForestRescuePending);
        }

        private void UpdateActiveTreatmentProgress()
        {
            if (State.IsGotoroFlashback
                && _stressLoadService.GetActiveTreatmentEpisodeId() != StressEpisodes.GotoroFlashback)
            {
                State.DeferredGotoroShelterSeconds = Math.Max(
                    State.DeferredGotoroShelterSeconds,
                    State.ForestShelterSeconds);
            }

            var episodeId = _stressLoadService.GetActiveTreatmentEpisodeId();
            if (episodeId is not (StressEpisodes.GotoroFlashback or StressEpisodes.AnxietySpike))
                return;

            if (!_stressLoadService.HasActiveTreatment())
                return;

            _episodeQuestProgressService?.OnFlashbackShelterUpdated(
                State.ForestShelterSeconds,
                episodeId);
        }
    }
}
