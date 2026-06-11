using System;
using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Delegates;
using StardewValley.Triggers;

namespace HarveyStressMeter.Services
{
    /// <summary>
    /// Dialogue $action handlers — единственный игровой путь смены состояния stress/social anxiety.
    /// </summary>
    public sealed class HarveyStressActionHandler
    {
        private readonly IMonitor _monitor;
        private readonly TreatmentService _treatmentService;
        private readonly TreatmentEpisodeService _episodeService;
        private readonly StressTreatmentReviewService _reviewService;
        private readonly StressDialogueService _dialogueService;
        private readonly SocialAnxietyTherapyService? _socialAnxietyTherapyService;

        public HarveyStressActionHandler(
            IMonitor monitor,
            TreatmentService treatmentService,
            TreatmentEpisodeService episodeService,
            StressTreatmentReviewService reviewService,
            StressDialogueService dialogueService,
            SocialAnxietyTherapyService? socialAnxietyTherapyService)
        {
            _monitor = monitor;
            _treatmentService = treatmentService;
            _episodeService = episodeService;
            _reviewService = reviewService;
            _dialogueService = dialogueService;
            _socialAnxietyTherapyService = socialAnxietyTherapyService;
        }

        public void RegisterTriggerActions()
        {
            TriggerActionManager.RegisterAction(HarveyStressActions.StartTreatment, OnStartTreatment);
            TriggerActionManager.RegisterAction(HarveyStressActions.CompleteReview, OnCompleteReview);
            TriggerActionManager.RegisterAction(HarveyStressActions.SocialAnxietyStart, OnSocialAnxietyStart);
            TriggerActionManager.RegisterAction(HarveyStressActions.SocialAnxietyStartReview, OnSocialAnxietyStartReview);
            TriggerActionManager.RegisterAction(HarveyStressActions.SocialAnxietyComplete, OnSocialAnxietyComplete);

            _monitor.Log(
                "[HarveyStressAction] Registered trigger actions: StartTreatment, CompleteReview, SocialAnxiety_*",
                LogLevel.Debug);
        }

        public bool OnStartTreatment(string[] args, TriggerActionContext context, out string error)
        {
            error = string.Empty;

            if (!Context.IsWorldReady)
            {
                error = "world not ready";
                return false;
            }

            string? buffId = ParseBuffId(args, out error);
            if (buffId == null)
                return false;

            if (string.Equals(buffId, BuffIds.Social, StringComparison.Ordinal))
            {
                error = "use HarveyStress_SocialAnxiety_Start for social anxiety";
                return false;
            }

            bool applied = _dialogueService.TryApplyStartTreatmentFromAction(buffId, out error);
            if (applied)
            {
                MarkAndLog(HarveyStressActions.StartTreatment, buffId, $"buff={buffId}");
            }

            return applied;
        }

        public bool OnCompleteReview(string[] args, TriggerActionContext context, out string error)
        {
            error = string.Empty;

            if (!Context.IsWorldReady)
            {
                error = "world not ready";
                return false;
            }

            string? buffId = ParseBuffId(args, out error);
            if (buffId == null)
                return false;

            if (string.Equals(buffId, BuffIds.Social, StringComparison.Ordinal))
            {
                error = "use HarveyStress_SocialAnxiety_Complete for social anxiety";
                return false;
            }

            bool applied = _reviewService.TryCompleteReviewFromAction(buffId, out error);
            if (applied)
            {
                MarkAndLog(HarveyStressActions.CompleteReview, buffId, $"buff={buffId}");
            }

            return applied;
        }

        public bool OnSocialAnxietyStart(string[] args, TriggerActionContext context, out string error)
        {
            error = string.Empty;

            if (!Context.IsWorldReady)
            {
                error = "world not ready";
                return false;
            }

            bool applied = _dialogueService.TryApplyStartTreatmentFromAction(BuffIds.Social, out error);
            if (applied)
            {
                _socialAnxietyTherapyService?.StartTherapy();
                MarkAndLog(HarveyStressActions.SocialAnxietyStart, "topicStressSocial", "social therapy start");
            }

            return applied;
        }

        public bool OnSocialAnxietyStartReview(string[] args, TriggerActionContext context, out string error)
        {
            error = string.Empty;

            if (!Context.IsWorldReady)
            {
                error = "world not ready";
                return false;
            }

            _socialAnxietyTherapyService?.OnFollowupDialogueStarted();
            MarkAndLog(
                HarveyStressActions.SocialAnxietyStartReview,
                StressDialogueKeys.SocialAnxietyReview,
                "awaiting follow-up");
            return true;
        }

        public bool OnSocialAnxietyComplete(string[] args, TriggerActionContext context, out string error)
        {
            error = string.Empty;

            if (!Context.IsWorldReady)
            {
                error = "world not ready";
                return false;
            }

            bool applied = _reviewService.TryCompleteReviewFromAction(BuffIds.Social, out error);
            if (!applied)
                return false;

            _socialAnxietyTherapyService?.OnQuestCompleted();
            MarkAndLog(
                HarveyStressActions.SocialAnxietyComplete,
                StressDialogueKeys.SocialAnxietyReview,
                "social quest complete");
            return true;
        }

        private void MarkAndLog(string action, string key, string detail)
        {
            HarveyInteractionLogger.LogTalk(_monitor, "Stress", key, action, consumed: false, detail);
        }

        private static string? ParseBuffId(string[] args, out string error)
        {
            error = string.Empty;

            if (ArgUtility.TryGet(args, 1, out string buffId, out error, allowBlank: false)
                && buffId.StartsWith("buff", StringComparison.OrdinalIgnoreCase))
            {
                return buffId;
            }

            if (ArgUtility.TryGet(args, 0, out buffId, out error, allowBlank: false)
                && buffId.StartsWith("buff", StringComparison.OrdinalIgnoreCase))
            {
                return buffId;
            }

            error = string.IsNullOrWhiteSpace(error)
                ? "expected buffId argument (buff*)"
                : error;
            return null;
        }
    }
}
