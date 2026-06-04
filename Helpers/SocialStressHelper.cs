using StardewValley;

namespace HarveyStressMeter.Helpers
{
    /// <summary>Расчёт социальной усталости за день перед выдачей debuff Social.</summary>
    public static class SocialStressHelper
    {
        public const int MaxExposure = 100;
        public const int ThresholdWarning = 40;
        public const int ThresholdPause = 70;
        public const int ThresholdOverload = 90;
        public const int ThresholdDebuff = 100;

        public const int HarveyRecoveryIntervalSeconds = 10;
        public const int HomeRecoveryIntervalSeconds = 20;
        public const int HomeRecoveryStartTime = 1800;

        public const float DatingMarriedGainMultiplier = 0.75f;
        public const float OtherStressDebuffGainMultiplier = 1.15f;

        public const int Threshold40Flag = 1;
        public const int Threshold70Flag = 2;
        public const int Threshold90Flag = 4;

        public static bool IsQualifyingNpc(NPC? npc)
        {
            if (npc == null || npc.IsMonster || npc.IsInvisible || !npc.IsVillager)
                return false;

            return !string.Equals(npc.Name, "Harvey", System.StringComparison.Ordinal);
        }

        public static int GetBaseExposureGain(string npcName)
        {
            if (!Game1.player.friendshipData.TryGetValue(npcName, out var friendship))
                return 25;

            return friendship.Points switch
            {
                >= 750 => 0,
                >= 500 => 8,
                >= 250 => 14,
                _ => 22,
            };
        }

        public static float GetAccumulationMultiplier(bool hasOtherStressDebuff)
        {
            float mult = 1f;

            if (HarveyFriendshipHelper.IsDatingHarvey() || HarveyFriendshipHelper.IsMarriedToHarvey())
                mult *= DatingMarriedGainMultiplier;

            if (hasOtherStressDebuff)
                mult *= OtherStressDebuffGainMultiplier;

            return mult;
        }

        public static int ApplyAccumulationMultiplier(int baseGain, float multiplier)
            => System.Math.Max(0, (int)System.MathF.Round(baseGain * multiplier));

        public static string GetCompactStatusLabel(int exposure, bool socialDebuffActive)
        {
            if (socialDebuffActive)
                return "Социальный срыв";

            return exposure switch
            {
                >= ThresholdDebuff => "Социальный срыв",
                >= ThresholdOverload => "На грани",
                >= ThresholdPause => "Нужна пауза",
                >= ThresholdWarning => "Немного вымотана",
                _ => "Социально спокойно",
            };
        }

        public static int ThresholdFlagForExposure(int exposure) => exposure switch
        {
            >= ThresholdOverload => Threshold90Flag,
            >= ThresholdPause => Threshold70Flag,
            >= ThresholdWarning => Threshold40Flag,
            _ => 0,
        };

        public static string GetThresholdHudMessage(int threshold) => threshold switch
        {
            ThresholdWarning => "Общение начинает утомлять.",
            ThresholdPause => "Лучше сделать паузу перед новыми разговорами.",
            ThresholdOverload => "Ты на грани социальной перегрузки.",
            _ => "",
        };

        public static bool IsHomeRecoveryContext()
            => Game1.timeOfDay >= HomeRecoveryStartTime
               && Game1.player.currentLocation is StardewValley.Locations.FarmHouse;
    }

}
