using System;
using StardewValley;
using HarveyStressMeter.Constants;

namespace HarveyStressMeter.Helpers
{
    public static class DarknessRemissionHelper
    {
        public static int GetStep1EveningsRequired(Models.DarknessProgress d)
        {
            int required = d.DarknessTherapyEveningsRequired;
            return required > 0 ? required : DarknessLegacyHelper.Step1EveningsRequired;
        }

        public static int GetDaysInRemission(Models.DarknessProgress d, int todayDay)
        {
            if (!d.DarknessRemissionActive || d.DarknessRemissionStartDay < 0)
                return 0;

            return Math.Max(0, todayDay - d.DarknessRemissionStartDay);
        }

        public static bool IsNightGameTime(int timeOfDay)
            => timeOfDay >= 2000 || timeOfDay < 600;

        public static bool IsAfterMidnight(int timeOfDay)
            => timeOfDay >= 0 && timeOfDay < 600;

        public static bool IsAfterTenPm(int timeOfDay)
            => timeOfDay >= 2200 && timeOfDay < 2600;

        public static int GetGameMinutesDelta(int previousTime, int currentTime)
        {
            if (previousTime < 0)
                return 0;

            if (currentTime >= previousTime)
                return currentTime - previousTime;

            // Переход через полночь (например 2530 → 610)
            return (2400 - previousTime) + Math.Max(0, currentTime - 600);
        }

        public static bool IsDarkOutdoorLocation(string? locationName)
        {
            if (string.IsNullOrEmpty(locationName))
                return false;

            return locationName is "Backwoods" or "Forest" or "Mountain" or "BusStop" or "Woods" or "Railroad";
        }

        public static bool IsDangerousNightLocation(string? locationName)
        {
            if (string.IsNullOrEmpty(locationName))
                return false;

            return GameStateHelper.IsStressfulWorkLocation(locationName)
                   || locationName is "Mine" or "SkullCave" or "VolcanoDungeon" or "Caldera" or "QuarryMine";
        }

        public static bool IsDarknessTriggerWindow(int timeOfDay)
            => timeOfDay >= 2200 || timeOfDay > 2600; // 22:00–02:00 (2600 = 2am wrap in vanilla)

        public static bool IsDarknessEnvironmentalTrigger(string? locationName, int timeOfDay)
        {
            if (!IsDarknessTriggerWindow(timeOfDay))
                return false;

            return IsDarkOutdoorLocation(locationName);
        }

        public static int ComputeRelapseEveningsRequired(Models.DarknessProgress d)
        {
            if (d.DarknessRemissionHadPassOut)
                return 3;

            if (d.DarknessRemissionHadNightDamage && d.DarknessRemissionHadDangerLocation)
                return 3;

            if (d.DarknessRemissionHadNightDamage || d.DarknessRemissionHadDangerLocation)
                return 2;

            return 1;
        }
    }
}
