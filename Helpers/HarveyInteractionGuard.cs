using StardewValley;

namespace HarveyStressMeter.Helpers
{
    /// <summary>
    /// Short-lived flag so only one Harvey mod system owns a dialogue cycle (Stress vs Injury).
    /// </summary>
    public static class HarveyInteractionGuard
    {
        public const string ConsumedTickKey = "HarveyStressMeter/HarveyInteractionConsumedTick";
        public const string ConsumedReasonKey = "HarveyStressMeter/HarveyInteractionConsumedReason";

        /// <summary>~2 seconds at 60 ticks/sec — blocks chained vanilla/CP Harvey lines in the same click.</summary>
        public const int FreshTickWindow = 120;

        public static void MarkConsumed(string reason)
        {
            var data = Game1.player?.modData;
            if (data == null)
                return;

            data[ConsumedTickKey] = Game1.ticks.ToString();
            data[ConsumedReasonKey] = reason ?? string.Empty;
        }

        public static void ClearConsumed()
        {
            var data = Game1.player?.modData;
            if (data == null)
                return;

            data.Remove(ConsumedTickKey);
            data.Remove(ConsumedReasonKey);
        }

        public static bool TryGetConsumedReason(out string reason)
        {
            reason = string.Empty;
            var data = Game1.player?.modData;
            if (data == null || !IsConsumed(out reason))
                return false;

            return true;
        }

        public static bool IsConsumed(out string reason, int tickWindow = FreshTickWindow)
        {
            reason = string.Empty;
            var data = Game1.player?.modData;
            if (data == null
                || !data.TryGetValue(ConsumedTickKey, out var tickRaw)
                || !int.TryParse(tickRaw, out var consumedTick))
            {
                return false;
            }

            if (Game1.ticks - consumedTick > tickWindow)
                return false;

            data.TryGetValue(ConsumedReasonKey, out reason);
            return true;
        }

        public static bool IsConsumed(int tickWindow = FreshTickWindow)
            => IsConsumed(out _, tickWindow);
    }
}
