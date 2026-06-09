using StardewModdingAPI;

namespace HarveyStressMeter.Helpers
{
    public static class HarveyInteractionLogger
    {
        public static void LogTalk(
            IMonitor monitor,
            string owner,
            string? key,
            string? action,
            bool consumed,
            string? skipNote = null)
        {
            var message =
                $"[HarveyInteraction] owner={owner} key={key ?? "(none)"} action={action ?? "(none)"} consumed={consumed}";

            if (!string.IsNullOrEmpty(skipNote))
                message += $" | {skipNote}";

            monitor.Log(message, LogLevel.Info);
        }

        public static void LogSkipped(IMonitor monitor, string system, string reason)
        {
            monitor.Log($"[HarveyInteraction] {system} skipped: {reason}", LogLevel.Info);
        }
    }
}
