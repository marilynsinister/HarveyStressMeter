using HarveyStressMeter.Constants;
using HarveyStressMeter.Helpers;
using HarveyStressMeter.Models;
using StardewModdingAPI;
using StardewValley;

namespace HarveyStressMeter.Services
{
    /// <summary>
    /// Реплики Харви о прогрессе восстановления через HarveyCareTrust (не лечение, а навык).
    /// </summary>
    public sealed class HarveyCareTrustDialogueService
    {
        private readonly SaveData _data;
        private readonly HarveyCareTrustService _trustService;
        private readonly StressDialogueService _stressDialogueService;
        private readonly IMonitor _monitor;

        public HarveyCareTrustDialogueService(
            SaveData data,
            HarveyCareTrustService trustService,
            StressDialogueService stressDialogueService,
            IMonitor monitor)
        {
            _data = data;
            _trustService = trustService;
            _stressDialogueService = stressDialogueService;
            _monitor = monitor;
        }

        /// <summary>
        /// Выбирает trust-реплику при разговоре с Харви. Не перебивает StartEpisode / Review.
        /// </summary>
        public bool TrySelectTrustProgressDialogue(
            EpisodeSelectionResult episodeSelection,
            out string? dialogueKey,
            out string? dialogueText)
        {
            dialogueKey = null;
            dialogueText = null;

            if (episodeSelection.Action == EpisodeSelectionAction.StartEpisode
                || episodeSelection.Action == EpisodeSelectionAction.AwaitingReview)
            {
                return false;
            }

            if (TrySelectRescueFollowUp(out dialogueKey) && dialogueKey != null)
            {
                dialogueText = _stressDialogueService.GetDialogueByKey(dialogueKey);
                if (dialogueText != null)
                {
                    MarkShown(dialogueKey);
                    ClearRescueTopic(dialogueKey);
                    LogSelected(dialogueKey, "forest rescue follow-up");
                    return true;
                }
            }

            if (_trustService.State.SuccessfulAssignments >= 2
                && !WasShown(StressDialogueKeys.TrustRecoveryRepeated))
            {
                dialogueKey = StressDialogueKeys.TrustRecoveryRepeated;
                dialogueText = _stressDialogueService.GetDialogueByKey(dialogueKey);
                if (dialogueText != null)
                {
                    MarkShown(dialogueKey);
                    LogSelected(dialogueKey, "repeated successful recovery");
                    return true;
                }
            }

            var trustLevel = _trustService.GetTrustLevel();
            if (HarveyFriendshipHelper.IsMarriedToHarvey()
                && trustLevel >= HarveyCareTrustLevels.SafePerson
                && !WasShown(StressDialogueKeys.TrustMarriedAnchor))
            {
                dialogueKey = StressDialogueKeys.TrustMarriedAnchor;
            }
            else if (HarveyFriendshipHelper.IsDatingHarvey()
                     && trustLevel >= HarveyCareTrustLevels.SafePerson
                     && !WasShown(StressDialogueKeys.TrustDatingGrounding))
            {
                dialogueKey = StressDialogueKeys.TrustDatingGrounding;
            }
            else if (_trustService.IsHarveySafePersonUnlocked()
                     && !HarveyFriendshipHelper.IsDatingHarvey()
                     && !HarveyFriendshipHelper.IsMarriedToHarvey()
                     && !WasShown(StressDialogueKeys.TrustSafePerson))
            {
                dialogueKey = StressDialogueKeys.TrustSafePerson;
            }
            else if (trustLevel >= HarveyCareTrustLevels.TrustedDoctor
                     && !WasShown(StressDialogueKeys.TrustTrustedDoctor))
            {
                dialogueKey = StressDialogueKeys.TrustTrustedDoctor;
            }
            else if (trustLevel >= HarveyCareTrustLevels.FamiliarDoctor
                     && !WasShown(StressDialogueKeys.TrustEarlyProfessional))
            {
                dialogueKey = StressDialogueKeys.TrustEarlyProfessional;
            }

            if (dialogueKey == null)
                return false;

            dialogueText = _stressDialogueService.GetDialogueByKey(dialogueKey);
            if (dialogueText == null)
                return false;

            MarkShown(dialogueKey);
            LogSelected(dialogueKey, $"trust level {trustLevel}");
            return true;
        }

        public void QueueRescueFollowUpTopic(string tier)
        {
            var topic = tier switch
            {
                FlashbackRescueTiers.Married => TopicIds.TrustRescueMarried,
                FlashbackRescueTiers.Dating => TopicIds.TrustRescueDating,
                FlashbackRescueTiers.HighTrust => TopicIds.TrustRescueHighTrust,
                _ => TopicIds.TrustRescueMidTrust,
            };

            if (!ConversationHelper.HasTopic(topic))
                ConversationHelper.AddTopic(topic, 7);

            _monitor.Log($"[TrustDialogue] Queued rescue follow-up topic {topic} (tier={tier})", LogLevel.Debug);
        }

        private bool TrySelectRescueFollowUp(out string? dialogueKey)
        {
            if (ConversationHelper.HasTopic(TopicIds.TrustRescueMarried))
            {
                dialogueKey = StressDialogueKeys.TrustRescueForTier(FlashbackRescueTiers.Married);
                return true;
            }

            if (ConversationHelper.HasTopic(TopicIds.TrustRescueDating))
            {
                dialogueKey = StressDialogueKeys.TrustRescueForTier(FlashbackRescueTiers.Dating);
                return true;
            }

            if (ConversationHelper.HasTopic(TopicIds.TrustRescueHighTrust))
            {
                dialogueKey = StressDialogueKeys.TrustRescueForTier(FlashbackRescueTiers.HighTrust);
                return true;
            }

            if (ConversationHelper.HasTopic(TopicIds.TrustRescueMidTrust))
            {
                dialogueKey = StressDialogueKeys.TrustRescueForTier(FlashbackRescueTiers.MidTrust);
                return true;
            }

            dialogueKey = null;
            return false;
        }

        private static void ClearRescueTopic(string dialogueKey)
        {
            if (dialogueKey.Contains(FlashbackRescueTiers.Married, StringComparison.Ordinal))
                ConversationHelper.RemoveTopic(TopicIds.TrustRescueMarried);
            else if (dialogueKey.Contains(FlashbackRescueTiers.Dating, StringComparison.Ordinal))
                ConversationHelper.RemoveTopic(TopicIds.TrustRescueDating);
            else if (dialogueKey.Contains(FlashbackRescueTiers.HighTrust, StringComparison.Ordinal))
                ConversationHelper.RemoveTopic(TopicIds.TrustRescueHighTrust);
            else if (dialogueKey.Contains(FlashbackRescueTiers.MidTrust, StringComparison.Ordinal))
                ConversationHelper.RemoveTopic(TopicIds.TrustRescueMidTrust);
        }

        private bool WasShown(string key)
            => _data.HarveyCareTrust.ShownCareTrustDialogueKeys.Contains(key);

        private void MarkShown(string key)
            => _data.HarveyCareTrust.ShownCareTrustDialogueKeys.Add(key);

        private void LogSelected(string key, string reason)
            => _monitor.Log($"[TrustDialogue] Selected {key} ({reason})", LogLevel.Debug);
    }
}
