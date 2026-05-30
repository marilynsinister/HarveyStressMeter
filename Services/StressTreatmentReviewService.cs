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

        public void OnReviewDialogueClosed()
        {
            if (!string.IsNullOrEmpty(_pendingEpisodeIdForCompletion))
            {
                if (!EnsurePipelineGuard(nameof(OnReviewDialogueClosed), requireDialogueBox: false))
                    return;

                var episodeId = _pendingEpisodeIdForCompletion;
                var fallbackBuffId = _pendingBuffIdForCompletion;
                _pendingEpisodeIdForCompletion = null;
                _pendingBuffIdForCompletion = null;

                _monitor.Log(
                    $"[StressTreatmentReview] Финальное завершение TreatmentEpisode после реплики Харви: {episodeId}",
                    LogLevel.Info);

                if (!_episodeService.CompleteTreatmentEpisode(episodeId)
                    && !string.IsNullOrEmpty(fallbackBuffId))
                {
                    _monitor.Log(
                        $"[StressTreatmentReview] Episode complete failed — fallback legacy buff={fallbackBuffId}",
                        LogLevel.Warn);
                    _treatmentService.CompleteTreatment(fallbackBuffId, "Лечение завершено.");
                }

                return;
            }

            if (string.IsNullOrEmpty(_pendingBuffIdForCompletion))
                return;

            if (!EnsurePipelineGuard(nameof(OnReviewDialogueClosed), requireDialogueBox: false))
                return;

            var buffId = _pendingBuffIdForCompletion;
            _pendingBuffIdForCompletion = null;

            _monitor.Log(
                $"[StressTreatmentReview] Финальное завершение legacy лечения после реплики Харви: {buffId}",
                LogLevel.Info);
            _treatmentService.CompleteTreatment(buffId, "Лечение завершено.");
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
