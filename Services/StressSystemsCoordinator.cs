using HarveyStressMeter.Constants;
using HarveyStressMeter.Models;
using StardewModdingAPI;
using StardewValley;

namespace HarveyStressMeter.Services
{
    /// <summary>
    /// Единые правила приоритета StressLoad / ThunderFlashback / Rescue / TreatmentEpisode / Trust.
    /// </summary>
    public sealed class StressSystemsCoordinator
    {
        private readonly SaveData _data;
        private readonly StressLoadService _stressLoadService;
        private readonly TreatmentEpisodeService _episodeService;
        private readonly ThunderFlashbackService _thunderFlashbackService;
        private readonly IMonitor _monitor;

        public StressSystemsCoordinator(
            SaveData data,
            StressLoadService stressLoadService,
            TreatmentEpisodeService episodeService,
            ThunderFlashbackService thunderFlashbackService,
            IMonitor monitor)
        {
            _data = data;
            _stressLoadService = stressLoadService;
            _episodeService = episodeService;
            _thunderFlashbackService = thunderFlashbackService;
            _monitor = monitor;
        }

        public string? ActiveTreatmentEpisodeId =>
            _episodeService.GetActiveTreatmentEpisode()?.EpisodeId
            ?? _stressLoadService.GetActiveTreatmentEpisodeId();

        public bool HasActiveTreatmentEpisode => _episodeService.HasActiveTreatmentEpisode();

        public bool IsAwaitingHarveyReview =>
            _episodeService.GetTreatmentAwaitingReview() != null
            || _stressLoadService.IsAwaitingHarveyReview();

        public bool IsGotoroAmbientActive => _data.StressLoad.GotoroFlashbackActive;

        public bool IsGotoroRuntimeFlashbackActive =>
            _thunderFlashbackService.State.IsActive
            && _thunderFlashbackService.State.IsGotoroFlashback;

        public bool IsActiveGotoroTreatmentEpisode =>
            string.Equals(
                ActiveTreatmentEpisodeId,
                StressEpisodes.GotoroFlashback,
                StringComparison.Ordinal);

        public GotoroIntegrationMode GetGotoroIntegrationMode()
        {
            if (!IsGotoroAmbientActive && !IsGotoroRuntimeFlashbackActive)
                return GotoroIntegrationMode.Inactive;

            if (IsActiveGotoroTreatmentEpisode)
                return GotoroIntegrationMode.ActiveGotoroTreatment;

            if (HasActiveTreatmentEpisode)
                return GotoroIntegrationMode.OverlayOnOtherTreatment;

            if (IsAwaitingHarveyReview)
                return GotoroIntegrationMode.OverlayWhileAwaitingReview;

            return GotoroIntegrationMode.ReadyToStartGotoroEpisode;
        }

        public bool CanStartTreatmentEpisode(string episodeId)
        {
            if (HasActiveTreatmentEpisode)
            {
                _monitor.Log(
                    $"[StressIntegration] Block StartTreatmentEpisode({episodeId}): active={ActiveTreatmentEpisodeId}",
                    LogLevel.Debug);
                return false;
            }

            if (IsAwaitingHarveyReview)
            {
                _monitor.Log(
                    $"[StressIntegration] Block StartTreatmentEpisode({episodeId}): awaiting review",
                    LogLevel.Debug);
                return false;
            }

            return true;
        }

        public bool ShouldDeferGotoroEpisodeCandidate()
            => IsGotoroAmbientActive
               && HasActiveTreatmentEpisode
               && !IsActiveGotoroTreatmentEpisode;

        public void OnGotoroFlashbackTriggered(bool isGotoro)
        {
            if (!isGotoro)
                return;

            var mode = GetGotoroIntegrationMode();
            _monitor.Log(
                $"[StressIntegration] Gotoro flashback triggered, mode={mode}, " +
                $"activeEpisode={ActiveTreatmentEpisodeId ?? "(none)"}, " +
                $"awaitingReview={IsAwaitingHarveyReview}",
                LogLevel.Info);

            if (mode is GotoroIntegrationMode.OverlayOnOtherTreatment
                or GotoroIntegrationMode.OverlayWhileAwaitingReview)
            {
                _monitor.Log(
                    "[StressIntegration] Gotoro adds StressCause/load only — no second treatment episode.",
                    LogLevel.Debug);
            }
        }

        /// <summary>
        /// После StabilizeFlashback: никогда CompleteTreatmentEpisode; review только для active Gotoro episode.
        /// </summary>
        public FlashbackStabilizationOutcome OnFlashbackStabilized(
            bool isGotoroFlashback,
            int forestShelterSeconds,
            Action<int>? applyStressDecay = null)
        {
            var mode = GetGotoroIntegrationMode();
            var decayAmount = isGotoroFlashback ? 20 : 12;
            if (applyStressDecay != null)
                applyStressDecay(decayAmount);

            var outcome = new FlashbackStabilizationOutcome
            {
                IntegrationMode = mode,
                IsGotoroFlashback = isGotoroFlashback,
                ForestShelterSeconds = forestShelterSeconds,
                AppliedStressDecay = decayAmount,
            };

            if (IsActiveGotoroTreatmentEpisode)
            {
                outcome.MarkedReadyForReview = true;
                outcome.ReadyForReviewEpisodeId = StressEpisodes.GotoroFlashback;
            }
            else if (string.Equals(
                ActiveTreatmentEpisodeId,
                StressEpisodes.AnxietySpike,
                StringComparison.Ordinal))
            {
                outcome.MarkedReadyForReview = true;
                outcome.ReadyForReviewEpisodeId = StressEpisodes.AnxietySpike;
            }
            else if (isGotoroFlashback)
            {
                outcome.DeferredEpisodeStart = true;
                _monitor.Log(
                    "[StressIntegration] Gotoro stabilized without active Gotoro episode — " +
                    "candidate remains for Harvey dialogue; no quest started.",
                    LogLevel.Debug);
            }

            _monitor.Log(
                $"[StressIntegration] Flashback stabilized: mode={mode}, review={outcome.MarkedReadyForReview}, " +
                $"deferredStart={outcome.DeferredEpisodeStart}",
                LogLevel.Info);

            return outcome;
        }

        /// <summary>
        /// После rescue: stress/shelter only; review только если active Gotoro episode; без новых квестов.
        /// </summary>
        public RescueIntegrationOutcome OnRescueCompleted(
            string tier,
            int stressReduced,
            int shelterBonusSeconds,
            bool flashbackStabilized)
        {
            var mode = GetGotoroIntegrationMode();
            var outcome = new RescueIntegrationOutcome
            {
                Tier = tier,
                IntegrationMode = mode,
                StressReduced = stressReduced,
                ShelterBonusSeconds = shelterBonusSeconds,
                FlashbackStabilized = flashbackStabilized,
            };

            if (IsActiveGotoroTreatmentEpisode
                && (flashbackStabilized
                    || !_thunderFlashbackService.State.IsActive))
            {
                outcome.MarkedReadyForReview = true;
                outcome.ReadyForReviewEpisodeId = StressEpisodes.GotoroFlashback;
            }
            else if (IsGotoroAmbientActive && !HasActiveTreatmentEpisode)
            {
                outcome.DeferredEpisodeStart = true;
            }

            _monitor.Log(
                $"[StressIntegration] Rescue completed: tier={tier}, mode={mode}, " +
                $"stabilized={flashbackStabilized}, review={outcome.MarkedReadyForReview}, " +
                $"deferred={outcome.DeferredEpisodeStart}",
                LogLevel.Info);

            return outcome;
        }

        public string BuildIntegrationSnapshot()
        {
            var mode = GetGotoroIntegrationMode();
            var selection = _episodeService.EvaluateSelection();
            return $"""
                === Stress systems integration ===
                Gotoro mode: {mode}
                Gotoro ambient: {IsGotoroAmbientActive}
                Thunder runtime: {IsGotoroRuntimeFlashbackActive}
                Active treatment: {ActiveTreatmentEpisodeId ?? "(none)"}
                Awaiting review: {IsAwaitingHarveyReview}
                Candidate episode: {_stressLoadService.GetCandidateEpisode() ?? "(none)"}
                Selection: {selection.Action} / {selection.EpisodeId ?? "(none)"}
                Selection reason: {selection.Reason ?? "(none)"}
                """;
        }
    }

    public enum GotoroIntegrationMode
    {
        Inactive,
        ReadyToStartGotoroEpisode,
        ActiveGotoroTreatment,
        OverlayOnOtherTreatment,
        OverlayWhileAwaitingReview,
    }

    public sealed class FlashbackStabilizationOutcome
    {
        public GotoroIntegrationMode IntegrationMode { get; init; }
        public bool IsGotoroFlashback { get; init; }
        public int ForestShelterSeconds { get; init; }
        public int AppliedStressDecay { get; init; }
        public bool MarkedReadyForReview { get; set; }
        public string? ReadyForReviewEpisodeId { get; set; }
        public bool DeferredEpisodeStart { get; set; }
    }

    public sealed class RescueIntegrationOutcome
    {
        public string Tier { get; init; } = "";
        public GotoroIntegrationMode IntegrationMode { get; init; }
        public int StressReduced { get; init; }
        public int ShelterBonusSeconds { get; init; }
        public bool FlashbackStabilized { get; init; }
        public bool MarkedReadyForReview { get; set; }
        public string? ReadyForReviewEpisodeId { get; set; }
        public bool DeferredEpisodeStart { get; set; }
    }
}
