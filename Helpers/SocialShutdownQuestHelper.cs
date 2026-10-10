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
        /// <summary>Минимум для «самого близкого» друга, когда 4 сердечек ещё ни с кем нет (1 сердечко).</summary>
        public const int MinClosestFriendPoints = 250;

        public static bool IsHarvey(string? npcName) =>
            string.Equals(npcName, "Harvey", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Порог «доверенного» человека. В начале игры 4 сердечек может не быть ни с кем —
        /// тогда доверенным считается самый близкий друг (от 1 сердечка), иначе путь Б был невыполним.
        /// null — подходящих друзей нет, остаётся только путь «рядом с Харви».
        /// </summary>
        public static int? GetTrustedThreshold()
        {
            int best = GetBestFriendPoints(out _);
            if (best >= TrustedFriendshipPoints)
                return TrustedFriendshipPoints;

            return best >= MinClosestFriendPoints ? best : null;
        }

        public static bool IsTrustedNpc(string npcName)
        {
            // Харви — отдельный путь «60 сек рядом», не «доверенный друг».
            if (IsHarvey(npcName))
                return false;

            int? threshold = GetTrustedThreshold();
            return threshold != null
                && Game1.player.friendshipData.TryGetValue(npcName, out var friendship)
                && friendship.Points >= threshold.Value;
        }

        public static bool IsUnfamiliarNpc(string npcName)
        {
            if (IsHarvey(npcName))
                return false;

            return !IsTrustedNpc(npcName);
        }

        /// <summary>Текст пути Б для журнала и плана: с кем можно поговорить прямо сейчас.</summary>
        public static string GetTrustedPathHint(bool informal = false)
        {
            int best = GetBestFriendPoints(out string? bestName);
            if (best >= TrustedFriendshipPoints)
                return informal ? "поговори с другом от 4 сердечек" : "поговорите с другом от 4 сердечек";

            if (best >= MinClosestFriendPoints && bestName != null)
                return informal
                    ? $"поговори с самым близким тебе человеком ({GetDisplayName(bestName)})"
                    : $"поговорите с самым близким вам человеком ({GetDisplayName(bestName)})";

            return informal
                ? "близких друзей пока нет — побудь рядом с Харви"
                : "близких друзей пока нет — побудьте рядом с Харви";
        }

        private static int GetBestFriendPoints(out string? bestName)
        {
            bestName = null;
            int best = 0;
            if (Game1.player?.friendshipData == null)
                return 0;

            foreach (var (name, friendship) in Game1.player.friendshipData.Pairs)
            {
                if (IsHarvey(name) || friendship.Points <= best)
                    continue;

                best = friendship.Points;
                bestName = name;
            }

            return best;
        }

        private static string GetDisplayName(string npcName) =>
            Game1.getCharacterFromName(npcName)?.displayName ?? npcName;
    }

}
