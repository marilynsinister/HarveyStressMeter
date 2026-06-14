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

        /// <summary>
        /// Ищет одно лечение, ожидающее review (по приоритету StressDebuffSelector).
        /// При repairStuck восстанавливает AwaitingHarveyReview из completed quest / objectives.
        /// </summary>
        public bool TryFindAnyTreatmentAwaitingReview(
            out string? episodeId,
            out string? buffId,
            bool repairStuck = false)
        {
            episodeId = null;
            buffId = null;

            if (repairStuck)
                RepairStuckReviewStates();

            var episode = _episodeService.GetTreatmentAwaitingReview();
            if (episode != null)
            {
                episodeId = episode.EpisodeId;
                buffId = ResolvePrimaryBuffId(episode);
                return true;
            }

            var treatment = GetTreatmentAwaitingReview();
            if (treatment == null)
                return false;

            buffId = treatment.BuffId;
            episodeId = TreatmentEpisodeDefinitions.ResolveEpisodeIdForQuest(treatment.QuestId);
            return true;
        }

        /// <summary>
        /// Пересканирует активные лечения и восстанавливает флаг awaiting review, если квест уже выполнен.
        /// </summary>
        public int RepairStuckReviewStates()
        {
            int anxietyRepaired = RepairStuckAnxietySpikeReview();

            var before = StressDebuffSelector.GetTreatmentsAwaitingReview(_stateService).Count;
            _treatmentService.SyncQuestsAndBuffs();
            var after = StressDebuffSelector.GetTreatmentsAwaitingReview(_stateService).Count;

            var repaired = after - before + anxietyRepaired;

            if (repaired > 0)
            {
                _monitor.Log(
                    $"[StressTreatmentReview] Repaired stuck review states: {before} → {after} awaiting review " +
                    $"(AnxietySpike fixes={anxietyRepaired})",
                    LogLevel.Info);
            }

            return repaired;
        }

        private int RepairStuckAnxietySpikeReview()
        {
            var episode = _episodeService.GetActiveTreatmentEpisode();
            if (episode == null
                || !string.Equals(episode.EpisodeId, StressEpisodes.AnxietySpike, StringComparison.Ordinal)
                || !episode.IsActiveEpisode()
                || episode.AwaitingHarveyReview)
            {
                return 0;
            }

            var treatment = !string.IsNullOrEmpty(episode.QuestId)
                ? _treatmentService.GetTreatmentByQuest(episode.QuestId)
                : null;
            if (treatment?.Progress == null
                || treatment.Progress.AnxietySafeSeconds < EpisodeQuestRules.AnxietySafeSecondsRequired)
            {
                return 0;
            }

            _monitor.Log(
                "[AnxietySpike] Objectives met but AwaitingHarveyReview=false — repairing review state.",
                LogLevel.Warn);
            _treatmentService.MarkTreatmentReadyForReviewByEpisode(StressEpisodes.AnxietySpike);
            return 1;
        }

        /// <summary>Programmatic review-реплика для episode или legacy treatment, ожидающего review.</summary>
        public bool TryPrepareEpisodeReviewDialogue(out string? buffId, out string? dialogueText, bool repairStuck = false)
        {
            buffId = null;
            dialogueText = null;

            if (HasPendingReviewCompletion)
                return false;

            if (!TryFindAnyTreatmentAwaitingReview(out var episodeId, out buffId, repairStuck))
                return false;

            if (!string.IsNullOrEmpty(episodeId))
            {
                dialogueText = _dialogueService?.GetReviewDialogueForEpisode(episodeId)
                    ?? StressQuestCopy.ReviewDialogue;
                _pendingEpisodeIdForCompletion = episodeId;
                _pendingBuffIdForCompletion = buffId;

                _monitor.Log(
                    $"[StressTreatmentReview] Prepared episode review dialogue (episode={episodeId}, buff={buffId})",
                    LogLevel.Info);

                return true;
            }

            if (string.IsNullOrEmpty(buffId))
                return false;

            dialogueText = StressQuestCopy.ReviewDialogue;
            _pendingBuffIdForCompletion = buffId;

            _monitor.Log(
                $"[StressTreatmentReview] Prepared legacy review dialogue (buff={buffId})",
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
                _monitor.Log(
                    $"[HarveyStress] CompleteReview action received without buffId: {error}",
                    LogLevel.Warn);
                return false;
            }

            _monitor.Log(
                $"[HarveyStress] CompleteReview action received: action={HarveyStressActions.CompleteReview}, " +
                $"buff={buffId}, episode={_pendingEpisodeIdForCompletion ?? "(pending)"}",
                LogLevel.Info);

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

        /// <summary>
        /// После закрытия programmatic review без $action — pending сохраняется, HUD-подсказка.
        /// Завершение: HarveyStress_CompleteReview / HarveyStress_SocialAnxiety_Complete.
        /// </summary>
        public void TryFallbackCompleteReviewAfterDialogue()
        {
            if (!HasPendingReviewCompletion)
                return;

            _monitor.Log(
                "[StressTreatmentReview] ⚠️ Programmatic review closed without $action " +
                $"(expected {HarveyStressActions.CompleteReview} or {HarveyStressActions.SocialAnxietyComplete}; " +
                $"pending buff={_pendingBuffIdForCompletion ?? "(none)"}, " +
                $"episode={_pendingEpisodeIdForCompletion ?? "(none)"}) — pending preserved, treatment unchanged",
                LogLevel.Warn);

            if (!string.IsNullOrEmpty(_pendingEpisodeIdForCompletion)
                && string.Equals(_pendingEpisodeIdForCompletion, StressEpisodes.AnxietySpike, StringComparison.Ordinal))
            {
                _monitor.Log(
                    "[HarveyStress] AnxietySpike stuck: review dialogue closed without completion action.",
                    LogLevel.Warn);
            }

            Game1.addHUDMessage(new HUDMessage(
                "Харви ждёт завершить осмотр. Поговорите с ним ещё раз.",
                HUDMessage.error_type));
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
                _monitor.Log($"[HarveyStress] Review completed: {episodeId}.", LogLevel.Info);

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
            _monitor.Log($"[HarveyStress] Review completed: {buffId}.", LogLevel.Info);
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
