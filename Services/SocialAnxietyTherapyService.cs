using System;
using System.Text;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;

namespace HarveyStressMeter.Services
{
    /// <summary>
    /// Терапия социальной тревожности: таймер 60 сек + programmatic follow-up с Харви
    /// (не зависит от дневного vanilla dialogue / Custom Kisses).
    /// </summary>
    public sealed class SocialAnxietyTherapyService
    {
        public const int HarveySecondsRequired = 60;

        private readonly IMonitor _monitor;
        private readonly SaveData _data;
        private readonly StateService _stateService;
        private readonly TreatmentService _treatmentService;
        private readonly QuestService _questService;
        private readonly TriggerService _triggerService;
        private readonly StressDialogueService _stressDialogueService;
        private readonly StressTreatmentReviewService _reviewService;

        public SocialAnxietyTherapyState State => _data.SocialAnxietyTherapy;

        public SocialAnxietyTherapyService(
            IMonitor monitor,
            SaveData data,
            StateService stateService,
            TreatmentService treatmentService,
            QuestService questService,
            TriggerService triggerService,
            StressDialogueService stressDialogueService,
            StressTreatmentReviewService reviewService)
        {
            _monitor = monitor;
            _data = data;
            _stateService = stateService;
            _treatmentService = treatmentService;
            _questService = questService;
            _triggerService = triggerService;
            _stressDialogueService = stressDialogueService;
            _reviewService = reviewService;
        }

        public bool IsTherapyActive =>
            State.Phase is SocialAnxietyTherapyPhase.TimerActive
                or SocialAnxietyTherapyPhase.TimerCompleted
                or SocialAnxietyTherapyPhase.ReadyToComplete
                or SocialAnxietyTherapyPhase.AwaitingHarveyFollowup;

        public bool IsReadyToComplete =>
            State.Phase is SocialAnxietyTherapyPhase.ReadyToComplete
                or SocialAnxietyTherapyPhase.AwaitingHarveyFollowup
            || GetSocialTreatment()?.AwaitingHarveyReview == true;

        public void StartTherapy()
        {
            State.Phase = SocialAnxietyTherapyPhase.TimerActive;
            State.TimerCompletedOn = null;
            State.ReadyToCompleteOn = null;
            State.TimerSecondsAtCompletion = 0;
            State.CompletionPath = "";

            _monitor.Log("[SocialAnxiety] Therapy started (TimerActive)", LogLevel.Info);
        }

        public void OnTimerSecondProgress(TreatmentProgress progress, bool harveyNearby)
        {
            if (!_stateService.HasActiveQuestState(QuestIds.Social))
                return;

            if (State.Phase == SocialAnxietyTherapyPhase.None)
                StartTherapy();

            if (IsReadyToComplete)
                return;

            if (progress.SecondsNearHarvey >= HarveySecondsRequired
                && State.Phase is SocialAnxietyTherapyPhase.TimerActive or SocialAnxietyTherapyPhase.TimerCompleted)
            {
                OnTimerReachedSixty(progress);
            }
        }

        public void OnTimerReachedSixty(TreatmentProgress progress)
        {
            if (State.Phase >= SocialAnxietyTherapyPhase.TimerCompleted
                && State.TimerSecondsAtCompletion >= HarveySecondsRequired)
            {
                return;
            }

            State.Phase = SocialAnxietyTherapyPhase.TimerCompleted;
            State.TimerCompletedOn = SDate.Now();
            State.TimerSecondsAtCompletion = Math.Max(progress.SecondsNearHarvey, HarveySecondsRequired);

            Game1.addHUDMessage(new HUDMessage(SocialAnxietyTherapyCopy.TimerCompletedHud, HUDMessage.newQuest_type));
            _triggerService.UpdateQuestDescription(progress);

            _monitor.Log(
                $"[SocialAnxiety] Timer reached 60/60 (saved TimerCompleted, seconds={State.TimerSecondsAtCompletion})",
                LogLevel.Info);
        }

        public void MarkReadyToComplete(TreatmentProgress progress, string completionPath)
        {
            if (IsReadyToComplete && State.Phase >= SocialAnxietyTherapyPhase.ReadyToComplete)
                return;

            if (progress.SecondsNearHarvey >= HarveySecondsRequired
                && State.Phase < SocialAnxietyTherapyPhase.TimerCompleted)
            {
                OnTimerReachedSixty(progress);
            }

            State.Phase = SocialAnxietyTherapyPhase.ReadyToComplete;
            State.ReadyToCompleteOn = SDate.Now();
            State.CompletionPath = completionPath;

            _treatmentService.MarkTreatmentReadyForReview(
                BuffIds.Social,
                SocialAnxietyTherapyCopy.ReadyForReviewHud,
                skipConversationTopic: true);

            UpdateReadyQuestJournal(progress);

            _monitor.Log(
                $"[SocialAnxiety] ReadyToComplete set (path={completionPath}, AwaitingHarveyReview=true, programmatic follow-up)",
                LogLevel.Info);
        }

        public void OnFollowupDialogueStarted()
        {
            if (!IsReadyToComplete)
                return;

            State.Phase = SocialAnxietyTherapyPhase.AwaitingHarveyFollowup;
            _monitor.Log("[SocialAnxiety] AwaitingHarveyFollowup — follow-up dialogue opened", LogLevel.Info);
        }

        public void OnQuestCompleted()
        {
            _monitor.Log("[SocialAnxiety] Quest completed — clearing therapy state", LogLevel.Info);
            Reset();
        }

        public void OnQuestCompletedIfTreatmentFinished()
        {
            // Internal helper — вызывается только из HarveyStress_SocialAnxiety_Complete action / debug.
            if (State.Phase == SocialAnxietyTherapyPhase.None)
                return;

            if (!_stateService.HasActiveQuestState(QuestIds.Social)
                && !_stateService.HasActiveTreatmentState(BuffIds.Social))
            {
                OnQuestCompleted();
            }
        }

        public void Reset()
        {
            State.Phase = SocialAnxietyTherapyPhase.None;
            State.TimerCompletedOn = null;
            State.ReadyToCompleteOn = null;
            State.TimerSecondsAtCompletion = 0;
            State.CompletionPath = "";
            _monitor.Log("[SocialAnxiety] Therapy state reset", LogLevel.Info);
        }

        public void RepairStateAfterLoad()
        {
            var treatment = GetSocialTreatment();
            if (treatment?.Progress == null || !treatment.TreatmentStarted)
            {
                if (State.Phase != SocialAnxietyTherapyPhase.None)
                    Reset();
                return;
            }

            if (treatment.AwaitingHarveyReview)
            {
                State.Phase = SocialAnxietyTherapyPhase.ReadyToComplete;
                State.CompletionPath = treatment.Progress.GetSocialCompletionPath();
                if (State.ReadyToCompleteOn == null)
                    State.ReadyToCompleteOn = treatment.ReadyForReviewDate ?? SDate.Now();

                _monitor.Log(
                    "[SocialAnxiety] Save load: restored ReadyToComplete from AwaitingHarveyReview",
                    LogLevel.Info);
                return;
            }

            if (treatment.Progress.SecondsNearHarvey >= HarveySecondsRequired
                || State.TimerSecondsAtCompletion >= HarveySecondsRequired)
            {
                State.Phase = SocialAnxietyTherapyPhase.TimerCompleted;
                State.TimerSecondsAtCompletion = Math.Max(
                    State.TimerSecondsAtCompletion,
                    treatment.Progress.SecondsNearHarvey);

                _monitor.Log(
                    "[SocialAnxiety] Save load: restored TimerCompleted",
                    LogLevel.Info);
                return;
            }

            if (State.Phase == SocialAnxietyTherapyPhase.None)
                State.Phase = SocialAnxietyTherapyPhase.TimerActive;
        }

        public void OnDayStarted()
        {
            if (!IsReadyToComplete)
                return;

            _monitor.Log(
                "[SocialAnxiety] New day while ReadyToComplete — follow-up still required (state preserved)",
                LogLevel.Info);
        }

        /// <summary>
        /// Harmony/checkAction: перехват взаимодействия с Харви до vanilla dialogue / Custom Kisses.
        /// </summary>
        public bool TryInterceptHarveyInteraction(NPC harvey, Farmer who, GameLocation location)
        {
            if (harvey.Name != "Harvey")
            {
                LogInterceptSkipped("target is not Harvey");
                return false;
            }

            if (!IsReadyToComplete)
            {
                LogInterceptSkipped(BuildNotReadyReason());
                return false;
            }

            if (!_stateService.HasActiveQuestState(QuestIds.Social))
            {
                LogInterceptSkipped("Social quest not active");
                return false;
            }

            if (GameStateHelper.IsEventActive())
            {
                LogInterceptSkipped("event active");
                return false;
            }

            if (Game1.activeClickableMenu != null)
            {
                LogInterceptSkipped($"menu open ({Game1.activeClickableMenu.GetType().Name})");
                return false;
            }

            if (harvey.currentLocation != location)
            {
                LogInterceptSkipped("Harvey not in current location");
                return false;
            }

            if (who.currentLocation != location)
            {
                LogInterceptSkipped("player not in Harvey location");
                return false;
            }

            if (!_stressDialogueService.TryShowProgrammaticReviewDialogue(BuffIds.Social))
            {
                LogInterceptSkipped("TryShowProgrammaticReviewDialogue returned false");
                return false;
            }

            OnFollowupDialogueStarted();
            HarveyInteractionLogger.LogTalk(
                _monitor,
                "Stress",
                StressDialogueKeys.SocialAnxietyReview,
                HarveyStressActions.SocialAnxietyComplete,
                HarveyInteractionGuard.IsConsumed(out _));
            _monitor.Log(
                "[SocialAnxiety] Harvey interaction intercepted for quest completion (programmatic review dialogue)",
                LogLevel.Info);
            return true;
        }

        public void DebugSetTimer(int seconds)
        {
            var treatment = GetSocialTreatment();
            if (treatment?.Progress == null)
            {
                _monitor.Log("[SocialAnxiety] debug set_timer: no active Social treatment", LogLevel.Warn);
                return;
            }

            if (State.Phase == SocialAnxietyTherapyPhase.None)
                State.Phase = SocialAnxietyTherapyPhase.TimerActive;

            treatment.Progress.SecondsNearHarvey = Math.Max(0, seconds);
            _triggerService.UpdateQuestDescription(treatment.Progress);

            if (seconds >= HarveySecondsRequired)
                OnTimerReachedSixty(treatment.Progress);
        }

        public void DebugForceReady()
        {
            var treatment = GetSocialTreatment();
            if (treatment?.Progress == null)
            {
                _monitor.Log("[SocialAnxiety] debug ready: no active Social treatment", LogLevel.Warn);
                return;
            }

            treatment.Progress.SecondsNearHarvey = Math.Max(treatment.Progress.SecondsNearHarvey, HarveySecondsRequired);
            treatment.Progress.SocialTalksAfterQuest = Math.Max(treatment.Progress.SocialTalksAfterQuest, 3);
            MarkReadyToComplete(treatment.Progress, "path1");
        }

        public void DebugForceComplete()
        {
            if (!_stateService.HasActiveTreatmentState(BuffIds.Social))
            {
                _monitor.Log("[SocialAnxiety] debug complete: no active Social treatment", LogLevel.Warn);
                return;
            }

            _treatmentService.CompleteTreatment(BuffIds.Social, "Лечение завершено (debug).");
            OnQuestCompleted();
        }

        public string BuildDebugSnapshot()
        {
            var treatment = GetSocialTreatment();
            var progress = treatment?.Progress;
            var sb = new StringBuilder();
            sb.AppendLine("=== Social Anxiety Therapy ===");
            sb.AppendLine($"Phase: {State.Phase}");
            sb.AppendLine($"TimerCompletedOn: {State.TimerCompletedOn?.ToString() ?? "(none)"}");
            sb.AppendLine($"ReadyToCompleteOn: {State.ReadyToCompleteOn?.ToString() ?? "(none)"}");
            sb.AppendLine($"TimerSecondsAtCompletion: {State.TimerSecondsAtCompletion}");
            sb.AppendLine($"CompletionPath: {State.CompletionPath ?? "(none)"}");
            sb.AppendLine($"QuestActive: {_stateService.HasActiveQuestState(QuestIds.Social)}");
            sb.AppendLine($"AwaitingHarveyReview: {treatment?.AwaitingHarveyReview ?? false}");
            sb.AppendLine($"SecondsNearHarvey: {progress?.SecondsNearHarvey ?? 0}");
            sb.AppendLine($"SocialTalksAfterQuest: {progress?.SocialTalksAfterQuest ?? 0}");
            sb.AppendLine($"IsReadyToComplete: {IsReadyToComplete}");
            sb.AppendLine($"ReviewPending: {_reviewService.HasPendingReviewCompletion}");
            return sb.ToString().TrimEnd();
        }

        private void UpdateReadyQuestJournal(TreatmentProgress progress)
        {
            var progressText = progress.GetSocialProgressText();
            _questService.UpdateQuest(
                QuestIds.Social,
                description:
                    "Харви предложил мягкую экспозицию: поговори с людьми и проведи время рядом с ним.\n\n" +
                    progressText,
                objective: SocialAnxietyTherapyCopy.ReadyForReviewObjective);
        }

        private TreatmentState? GetSocialTreatment()
            => _data.StressState.GetActiveTreatmentByQuest(QuestIds.Social)
               ?? _data.StressState.GetActiveTreatment(BuffIds.Social);

        private void LogInterceptSkipped(string reason)
        {
            if (!IsTherapyActive && GetSocialTreatment()?.AwaitingHarveyReview != true)
                return;

            _monitor.Log(
                $"[SocialAnxiety] Harvey interaction NOT intercepted: {reason} (phase={State.Phase})",
                LogLevel.Debug);
        }

        private string BuildNotReadyReason()
        {
            if (!_stateService.HasActiveQuestState(QuestIds.Social))
                return "Social quest inactive";

            if (State.Phase == SocialAnxietyTherapyPhase.TimerActive)
                return "timer still active (not ReadyToComplete)";

            if (State.Phase == SocialAnxietyTherapyPhase.TimerCompleted)
                return "timer done but quest objectives not complete yet";

            return $"phase={State.Phase}, AwaitingHarveyReview={GetSocialTreatment()?.AwaitingHarveyReview}";
        }
    }
}
