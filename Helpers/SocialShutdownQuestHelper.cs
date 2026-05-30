using StardewValley;

namespace HarveyStressMeter.Helpers
{
    /// <summary>Правила квеста SocialShutdown (эпизод «Не оставаться одной»).</summary>
    public static class SocialShutdownQuestHelper
    {
        public const int HarveySecondsRequired = 60;
        public const int MaxUnfamiliarTalksPerDay = 3;
        /// <summary>4+ сердечка дружбы.</summary>
        public const int TrustedFriendshipPoints = 1000;

        public static bool IsHarvey(string? npcName) =>
            string.Equals(npcName, "Harvey", StringComparison.OrdinalIgnoreCase);

        public static bool IsTrustedNpc(string npcName)
        {
            // Харви — отдельный путь «60 сек рядом», не «доверенный друг».
            if (IsHarvey(npcName))
                return false;

            return Game1.player.friendshipData.TryGetValue(npcName, out var friendship)
                && friendship.Points >= TrustedFriendshipPoints;
        }

        public static bool IsUnfamiliarNpc(string npcName)
        {
            if (IsHarvey(npcName))
                return false;

            if (!Game1.player.friendshipData.TryGetValue(npcName, out var friendship))
                return true;

            return friendship.Points < TrustedFriendshipPoints;
        }
    }

}
