using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace HarveyStressMeter.Helpers
{
    /// <summary>
    /// Единый guard: stress dialogue pipeline только в обычном разговоре с Харви вне событий.
    /// </summary>
    public static class StressDialoguePipelineGuard
    {
        public enum BlockReason
        {
            None = 0,
            NotWorldReady,
            CurrentEventActive,
            EventUp,
            NotDialogueBox,
            CurrentSpeakerNotHarvey,
        }

        /// <summary>
        /// Проверяет безопасный контекст для stress dialogue pipeline.
        /// </summary>
        /// <param name="blockReason">Причина отказа (None = разрешено).</param>
        /// <param name="requireDialogueBox">false при programmatic replace / post-close finalize.</param>
        /// <param name="requireHarveySpeaker">false при finalize после закрытия DialogueBox.</param>
        /// <param name="knownHarveyNpc">NPC из HandleHarveyDialogue, если currentSpeaker ненадёжен.</param>
        public static bool CanRun(
            out BlockReason blockReason,
            bool requireDialogueBox = true,
            bool requireHarveySpeaker = true,
            NPC? knownHarveyNpc = null)
        {
            if (!Context.IsWorldReady)
            {
                blockReason = BlockReason.NotWorldReady;
                return false;
            }

            if (Game1.CurrentEvent != null)
            {
                blockReason = BlockReason.CurrentEventActive;
                return false;
            }

            if (Game1.eventUp)
            {
                blockReason = BlockReason.EventUp;
                return false;
            }

            if (requireDialogueBox && Game1.activeClickableMenu is not DialogueBox)
            {
                blockReason = BlockReason.NotDialogueBox;
                return false;
            }

            if (requireHarveySpeaker && !IsHarveySpeaker(knownHarveyNpc))
            {
                blockReason = BlockReason.CurrentSpeakerNotHarvey;
                return false;
            }

            blockReason = BlockReason.None;
            return true;
        }

        public static void LogBlocked(IMonitor monitor, string caller, BlockReason blockReason)
        {
            if (blockReason == BlockReason.None)
                return;

            monitor.Log(
                $"[StressDialoguePipeline] Blocked ({caller}): {blockReason}",
                LogLevel.Debug);
        }

        public static void LogBypass(IMonitor monitor, string caller)
        {
            monitor.Log(
                $"[StressDialoguePipeline] Guard bypassed ({caller})",
                LogLevel.Debug);
        }

        private static bool IsHarveySpeaker(NPC? knownHarveyNpc)
        {
            if (knownHarveyNpc != null
                && string.Equals(knownHarveyNpc.Name, "Harvey", System.StringComparison.Ordinal))
            {
                return true;
            }

            return Game1.currentSpeaker is NPC npc
                && string.Equals(npc.Name, "Harvey", System.StringComparison.Ordinal);
        }
    }
}
