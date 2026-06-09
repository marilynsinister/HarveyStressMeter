using System;
using StardewModdingAPI;
using StardewValley;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;

namespace HarveyStressMeter.Services
{
    /// <summary>
    /// Финальная проверка Харви после выполнения условий лечения стресса.
    /// Episode-first: programmatic review → CompleteTreatmentEpisode.
    /// Legacy buff path: CP topic *ReadyForReview → CompleteTreatment(buffId).
    /// </summary>
    public class StressTreatmentReviewService
    {
        private readonly IMonitor _monitor;
        private readonly StateService _stateService;
        private readonly TreatmentService _treatmentService;
        private readonly TreatmentEpisodeService _episodeService;

        private StressDialogueService? _dialogueService;

        private string? _pendingBuffIdForCompletion;
        private string? _pendingEpisodeIdForCompletion;

        public bool HasPendingReviewCompletion =>
            !string.IsNullOrEmpty(_pendingBuffIdForCompletion)
            || !string.IsNullOrEmpty(_pendingEpisodeIdForCompletion);

        public string? PendingBuffIdForCompletion => _pendingBuffIdForCompletion;
        public string? PendingEpisodeIdForCompletion => _pendingEpisodeIdForCompletion;

        public StressTreatmentReviewService(
            IMonitor monitor,
            StateService stateService,
            TreatmentService treatmentService,
            TreatmentEpisodeService episodeService)
        {
            _monitor = monitor;
            _stateService = stateService;
            _treatmentService = treatmentService;
            _episodeService = episodeService;
        }

        public TreatmentState? GetTreatmentAwaitingReview()
            => StressDebuffSelector.GetPrimaryTreatmentAwaitingReview(_stateService);

        public TreatmentEpisodeState? GetTreatmentEpisodeAwaitingReview()
            => _episodeService.GetTreatmentAwaitingReview();

        public void SetDialogueService(StressDialogueService dialogueService)
            => _dialogueService = dialogueService;

        /// <summary>Programmatic review-реплика для episode или legacy treatment, ожидающего review.</summary>
        public bool TryPrepareEpisodeReviewDialogue(out string? buffId, out string? dialogueText)
        {
            buffId = null;
            dialogueText = null;

            if (HasPendingReviewCompletion)
                return false;

            var episode = _episodeService.GetTreatmentAwaitingReview();
            if (episode != null)
            {
                buffId = ResolvePrimaryBuffId(episode);
                dialogueText = _dialogueService?.GetReviewDialogueForEpisode(episode.EpisodeId)
                    ?? StressQuestCopy.ReviewDialogue;
                _pendingEpisodeIdForCompletion = episode.EpisodeId;

                _monitor.Log(
                    $"[StressTreatmentReview] Prepared episode review dialogue (episode={episode.EpisodeId})",
                    LogLevel.Info);

                return true;
            }

            var treatment = GetTreatmentAwaitingReview();
            if (treatment == null)
                return false;

            buffId = treatment.BuffId;
            var episodeId = TreatmentEpisodeDefinitions.ResolveEpisodeIdForQuest(treatment.QuestId);
            if (!string.IsNullOrEmpty(episodeId))
            {
                dialogueText = _dialogueService?.GetReviewDialogueForEpisode(episodeId)
                    ?? StressQuestCopy.ReviewDialogue;
                _pendingEpisodeIdForCompletion = episodeId;
            }
            else
            {
                dialogueText = StressQuestCopy.ReviewDialogue;
                _pendingBuffIdForCompletion = buffId;
            }

            _monitor.Log(
                $"[StressTreatmentReview] Prepared legacy review dialogue (buff={buffId}, episode={episodeId ?? "(none)"})",
                LogLevel.Info);

            return true;
        }

        /// <summary>
        /// Legacy: CP topic уже выставлен — vanilla/CP диалог, completion после close.
        /// </summary>
        public bool TryArmReviewCompletionOnHarveyTalk(out string? buffId)
        {
            buffId = null;

            if (HasPendingReviewCompletion)
                return false;

            if (_episodeService.GetTreatmentAwaitingReview() != null)
                return false;

            if (!EnsurePipelineGuard(nameof(TryArmReviewCompletionOnHarveyTalk)))
                return false;

            var treatment = GetTreatmentAwaitingReview();
            if (treatment == null)
                return false;

            var topicId = TreatmentTopics.GetReadyForReviewTopic(treatment.BuffId);
            if (topicId == null || !ConversationHelper.HasTopic(topicId))
            {
                _monitor.Log(
                    $"[StressTreatmentReview] Awaiting review for {treatment.BuffId}, but topic {topicId ?? "(null)"} missing — skip arm",
                    LogLevel.Warn);
                return false;
            }

            buffId = treatment.BuffId;
            _pendingBuffIdForCompletion = buffId;

            _monitor.Log(
                $"[StressTreatmentReview] Armed legacy completion after CP review dialogue (buff={buffId}, topic={topicId})",
                LogLevel.Info);

            return true;
        }

        /// <summary>Игровой путь: $action HarveyStress_CompleteReview / HarveyStress_SocialAnxiety_Complete.</summary>
        public bool TryCompleteReviewFromAction(string buffId, out string error)
        {
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(buffId))
            {
                error = "buffId missing";
                return false;
            }

            if (!HasPendingReviewCompletion)
            {
                if (!TryArmPendingForBuff(buffId))
                {
                    error = $"no review pending for {buffId}";
                    _monitor.Log($"[StressTreatmentReview] CompleteReview action skipped: {error}", LogLevel.Warn);
                    return false;
                }
            }

            if (!string.IsNullOrEmpty(_pendingBuffIdForCompletion)
                && !string.Equals(_pendingBuffIdForCompletion, buffId, StringComparison.Ordinal))
            {
                error = $"pending review is for {_pendingBuffIdForCompletion}, not {buffId}";
                return false;
            }

            return ApplyPendingReviewCompletion(fromAction: true, out error);
        }

        /// <summary>Repair/debug fallback после закрытия programmatic review без $action.</summary>
        public void TryFallbackCompleteReviewAfterDialogue()
        {
            if (!HasPendingReviewCompletion)
                return;

            if (HarveyInteractionGuard.IsConsumed(out _))
            {
                _monitor.Log(
                    "[StressTreatmentReview] Fallback complete skipped: HarveyStress action already consumed this cycle",
                    LogLevel.Debug);
                ClearPendingReview();
                return;
            }

            if (!EnsurePipelineGuard(nameof(TryFallbackCompleteReviewAfterDialogue), requireDialogueBox: false))
                return;

            _monitor.Log(
                "[StressTreatmentReview] ⚠️ Repair fallback: completing review without $action (legacy save/dialogue)",
                LogLevel.Warn);

            ApplyPendingReviewCompletion(fromAction: false, out _);
        }

        private bool ApplyPendingReviewCompletion(bool fromAction, out string error)
        {
            error = string.Empty;

            if (!string.IsNullOrEmpty(_pendingEpisodeIdForCompletion))
            {
                var episodeId = _pendingEpisodeIdForCompletion;
                var fallbackBuffId = _pendingBuffIdForCompletion;
                _pendingEpisodeIdForCompletion = null;
                _pendingBuffIdForCompletion = null;

                _monitor.Log(
                    $"[StressTreatmentReview] Complete review episode={episodeId} fromAction={fromAction}",
                    LogLevel.Info);

                if (!_episodeService.CompleteTreatmentEpisode(episodeId)
                    && !string.IsNullOrEmpty(fallbackBuffId))
                {
                    _treatmentService.CompleteTreatment(fallbackBuffId, "Лечение завершено.");
                }

                return true;
            }

            if (string.IsNullOrEmpty(_pendingBuffIdForCompletion))
            {
                error = "no pending review";
                return false;
            }

            var buffId = _pendingBuffIdForCompletion;
            _pendingBuffIdForCompletion = null;

            _monitor.Log(
                $"[StressTreatmentReview] Complete legacy review buff={buffId} fromAction={fromAction}",
                LogLevel.Info);
            _treatmentService.CompleteTreatment(buffId, "Лечение завершено.");
            return true;
        }

        private bool TryArmPendingForBuff(string buffId)
        {
            var episode = _episodeService.GetTreatmentAwaitingReview();
            if (episode != null)
            {
                var episodeBuff = ResolvePrimaryBuffId(episode);
                if (!string.Equals(episodeBuff, buffId, StringComparison.Ordinal))
                    return false;

                _pendingEpisodeIdForCompletion = episode.EpisodeId;
                return true;
            }

            var treatment = GetTreatmentAwaitingReview();
            if (treatment == null || !string.Equals(treatment.BuffId, buffId, StringComparison.Ordinal))
                return false;

            _pendingBuffIdForCompletion = buffId;
            return true;
        }

        public void ClearPendingReview()
        {
            _pendingBuffIdForCompletion = null;
            _pendingEpisodeIdForCompletion = null;
        }

        private static string? ResolvePrimaryBuffId(TreatmentEpisodeState episode)
        {
            if (!string.IsNullOrEmpty(episode.PrimaryCauseId)
                && StressCauses.CauseToBuff.TryGetValue(episode.PrimaryCauseId, out var buffId))
            {
                return buffId;
            }

            return TreatmentEpisodeDefinitions.ResolvePrimaryBuffId(
                episode.EpisodeId,
                episode.RelatedCauseIds);
        }

        private bool EnsurePipelineGuard(
            string caller,
            bool requireDialogueBox = true,
            NPC? knownHarveyNpc = null)
        {
            if (StressDialoguePipelineGuard.CanRun(
                    out var reason,
                    requireDialogueBox,
                    requireHarveySpeaker: false,
                    knownHarveyNpc))
            {
                return true;
            }

            StressDialoguePipelineGuard.LogBlocked(_monitor, caller, reason);
            return false;
        }
    }
}
