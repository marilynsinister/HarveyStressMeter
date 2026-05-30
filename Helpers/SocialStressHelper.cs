using StardewValley;

namespace HarveyStressMeter.Helpers
{
    /// <summary>Накопление социального дискомфорта перед выдачей debuff.</summary>
    public static class SocialStressHelper
    {
        public const int UnfriendlyFriendshipThreshold = 750;
        public const int ExposurePerTalk = 5;
        public const int DebuffThreshold = 20;
        public const int DailyExposureDecay = 8;

        public static bool IsQualifyingNpc(NPC? npc)
        {
            if (npc == null || npc.IsMonster || npc.IsInvisible || !npc.IsVillager)
                return false;

            if (string.Equals(npc.Name, "Harvey", System.StringComparison.Ordinal))
                return false;

            if (!Game1.player.friendshipData.TryGetValue(npc.Name, out var friendship))
                return true;

            return friendship.Points < UnfriendlyFriendshipThreshold;
        }

        public static int ApplyTrustMultiplier(int exposure, float trustMultiplier)
            => System.Math.Max(1, (int)System.MathF.Round(exposure * trustMultiplier));
    }

}
