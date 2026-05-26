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
    /// </summary>
    public sealed class ThunderFlashbackService
    {
        private const int FrightCheckIntervalMinutes = 100;

        private readonly SaveData _data;
        private readonly StressLoadService _stressLoadService;
        private readonly TreatmentService _treatmentService;
        private readonly BuffService _buffService;
        private readonly StateService _stateService;
        private readonly LightningFrightMessageService _messageService;
        private readonly ModConfig _config;
        private readonly IMonitor _monitor;
        private HarveyCareTrustService? _trustService;
        private HarveySafePersonAuraService? _safeAuraService;
        private StressSystemsCoordinator? _coordinator;

        private string? _lastLocationName;

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

        public void ResetDailyState()
        {
            State.WasTriggeredToday = false;
            State.WasStabilizedToday = false;
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
        }

        public void ResetFlashbackState()
        {
            State.IsActive = false;
            State.WasTriggeredToday = false;
            State.WasStabilizedToday = false;
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
            _lastLocationName = null;
        }

        public void OnTimeChanged(int oldTime, int newTime)
        {
            if (!CanEvaluateFrightRoll())
                return;

            if (Math.Abs(newTime - State.LastFrightCheckTime) < FrightCheckIntervalMinutes
                && newTime % FrightCheckIntervalMinutes != 0)
            {
                return;
            }

            State.LastFrightCheckTime = newTime;
            TryRollFright(force: false);
        }

        public void OnLocationChanged(string? previousLocation, string? newLocation)
        {
            if (!State.IsActive)
            {
                _lastLocationName = newLocation;
                return;
            }

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

            _lastLocationName = newLocation;
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

            if (!force && State.WasTriggeredToday)
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

        public void TriggerFlashback(bool force = false)
        {
            if (!force && !CanEvaluateFrightRoll())
                return;

            var intensity = CalculateIntensity();
            var isGotoro = intensity >= 70
                           || _data.StressLoad.WarTraumaFlag
                           || _stressLoadService.GetCurrentStressLoad() >= 75;

            State.IsActive = true;
            State.WasTriggeredToday = true;
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
                $"[ThunderFlashback] Triggered intensity={intensity}, gotoro={isGotoro}, " +
                $"load={_stressLoadService.GetCurrentStressLoad()}, episode={_stressLoadService.GetCandidateEpisode() ?? "(none)"}",
                LogLevel.Info);
        }

        /// <summary>Ускорение стабилизации после укрытия в лесу или помощи Харви.</summary>
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

        /// <summary>Бонус укрытия от safe person aura (Gotoro flashback, Харви рядом).</summary>
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

        public string BuildDebugSnapshot()
        {
            return $"""
                === Thunder Flashback ===
                IsActive: {State.IsActive}
                WasTriggeredToday: {State.WasTriggeredToday}
                WasStabilizedToday: {State.WasStabilizedToday}
                IsGotoroFlashback: {State.IsGotoroFlashback}
                Intensity: {State.LightningFrightIntensity}
                Trigger: {State.TriggerLocation ?? "(none)"} @ {State.TriggerTime}
                ForestShelter: {State.ForestShelterSeconds}/{State.RequiredForestShelterSeconds}
                EnteredForest: {State.EnteredForestDuringFlashback}
                LastHudMessage: {State.LastHudMessageTime}, cooldown={State.HudMessageCooldownMinutes}m
                Roll chance now: {CalculateFrightChancePercent()}%
                WarTraumaFlag: {_data.StressLoad.WarTraumaFlag}
                """;
        }

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

            return (int)Math.Clamp(chance, 0, 75);
        }

        private int CalculateIntensity()
        {
            int intensity = 35;

            if (_stressLoadService.GetCurrentStressLoad() >= 75)
                intensity += 30;
            else if (_stressLoadService.GetCurrentStressLoad() >= 50)
                intensity += 15;

            if (_buffService.HasBuff(BuffIds.Thunder))
                intensity += 15;

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
            var episodeId = _stressLoadService.GetActiveTreatmentEpisodeId();
            if (episodeId is not (StressEpisodes.GotoroFlashback or StressEpisodes.AnxietySpike))
                return;

            if (!_stressLoadService.HasActiveTreatment())
                return;

            var buffId = TreatmentEpisodeDefinitions.ResolvePrimaryBuffId(
                episodeId,
                _stressLoadService.GetActiveCauses().Keys);

            var treatment = _stateService.GetActiveTreatment(buffId);
            if (treatment == null || !treatment.TreatmentStarted || treatment.AwaitingHarveyReview)
                return;

            treatment.Progress ??= new TreatmentProgress();
            treatment.Progress.SecondsNearHarvey = Math.Max(
                treatment.Progress.SecondsNearHarvey,
                State.ForestShelterSeconds);
        }
    }
}

