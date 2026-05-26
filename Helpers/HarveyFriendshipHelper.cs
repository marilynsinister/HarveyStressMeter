using HarveyStressMeter.Constants;
using StardewValley;

namespace HarveyStressMeter.Helpers
{
    public static class HarveyFriendshipHelper
    {
        public static int GetHarveyHearts()
            => Game1.player.getFriendshipHeartLevelForNPC("Harvey");

        public static bool IsMarriedToHarvey()
            => string.Equals(Game1.player.spouse, "Harvey", StringComparison.OrdinalIgnoreCase);

        public static bool IsDatingHarvey()
        {
            if (!Game1.player.friendshipData.TryGetValue("Harvey", out var data))
                return false;

            return data.Status == FriendshipStatus.Dating;
        }

        /// <summary>Текущий tier forest rescue по отношениям (null = rescue недоступен).</summary>
        public static string? ResolveRescueTier(int minHeartsForForestRescue)
        {
            if (IsMarriedToHarvey())
                return FlashbackRescueTiers.Married;

            if (IsDatingHarvey())
                return FlashbackRescueTiers.Dating;

            var hearts = GetHarveyHearts();
            if (hearts >= 8)
                return FlashbackRescueTiers.HighTrust;

            if (hearts >= minHeartsForForestRescue)
                return FlashbackRescueTiers.MidTrust;

            return null;
        }

        public static double GetRescueChance(string tier, Models.ModConfig config)
        {
            if (string.Equals(tier, FlashbackRescueTiers.Married, StringComparison.Ordinal))
                return config.RescueChanceMarried;

            if (string.Equals(tier, FlashbackRescueTiers.Dating, StringComparison.Ordinal))
                return config.RescueChanceDating;

            if (string.Equals(tier, FlashbackRescueTiers.HighTrust, StringComparison.Ordinal))
                return config.RescueChanceHearts8To10;

            if (string.Equals(tier, FlashbackRescueTiers.MidTrust, StringComparison.Ordinal))
                return config.RescueChanceHearts6To7;

            return 0;
        }

        public static int GetStressReductionForTier(string tier) => tier switch
        {
            FlashbackRescueTiers.Married => 25,
            FlashbackRescueTiers.Dating => 20,
            FlashbackRescueTiers.HighTrust => 15,
            FlashbackRescueTiers.MidTrust => 10,
            _ => 10,
        };

        public static int GetForestShelterBonusForTier(string tier) => tier switch
        {
            FlashbackRescueTiers.Married => 45,
            FlashbackRescueTiers.Dating => 35,
            FlashbackRescueTiers.HighTrust => 25,
            FlashbackRescueTiers.MidTrust => 20,
            _ => 20,
        };

        public static int GetFriendshipBonusForTier(string tier) => tier switch
        {
            FlashbackRescueTiers.Married => 15,
            FlashbackRescueTiers.Dating => 12,
            FlashbackRescueTiers.HighTrust => 10,
            FlashbackRescueTiers.MidTrust => 5,
            _ => 5,
        };
    }
}
