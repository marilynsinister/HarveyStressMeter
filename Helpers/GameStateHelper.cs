using System;
using System.Linq;
using StardewValley;
using StardewValley.Tools;

namespace HarveyStressMeter.Helpers
{
    /// <summary>
    /// Вспомогательные методы для проверки состояния игры
    /// </summary>
    public static class GameStateHelper
    {
        /// <summary>
        /// Активен vanilla/CP event script (CurrentEvent или eventUp).
        /// Стрессовые диалоги, топики и StartTreatment во время события запрещены.
        /// </summary>
        public static bool IsEventActive()
            => Game1.CurrentEvent != null || Game1.eventUp;

        public static bool IsTimeBetween(int from, int to) 
            => IsTimeInRange(from, to);

        /// <summary>
        /// Линейный диапазон 26-hour time SDV (from &lt;= to). Например 2000–2600 = 20:00–2:00.
        /// </summary>
        public static bool IsTimeInRange(int from, int to)
            => Game1.timeOfDay >= from && Game1.timeOfDay <= to;

        /// <summary>
        /// Вечер и ночь до 2:00 (по умолчанию 20:00–2:00).
        /// </summary>
        public static bool IsEveningNight(int start = 2000, int end = 2600)
            => IsTimeInRange(start, end);

        /// <summary>
        /// После полуночи до 2:00 (2400–2600).
        /// </summary>
        public static bool IsAfterMidnightUntilTwoAm()
            => IsTimeInRange(2400, 2600);

        public static bool IsSeasonOneOf(params string[] seasons)
        {
            var currentSeason = Game1.currentSeason?.ToLowerInvariant();
            return seasons.Any(s => string.Equals(s, currentSeason, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsLocationOneOf(GameLocation loc, params string[] names)
        {
            string locationName = loc?.Name ?? "";
            return names.Any(n => string.Equals(n, locationName, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsWeatherOneOf(params string[] weathers)
        {
            bool storm = Game1.isLightning;
            bool rain = Game1.isRaining;
            bool wind = Game1.isDebrisWeather;
            bool snow = Game1.isSnowing;

            return (storm && weathers.Contains("Storm", StringComparer.OrdinalIgnoreCase))
                || (rain && weathers.Contains("Rain", StringComparer.OrdinalIgnoreCase))
                || (wind && weathers.Contains("Wind", StringComparer.OrdinalIgnoreCase))
                || (snow && weathers.Contains("Snow", StringComparer.OrdinalIgnoreCase));
        }

        public static bool HasHeavyTools(Farmer farmer)
        {
            foreach (var item in farmer.Items)
            {
                if (item is Pickaxe || item is Axe || item is Hoe) 
                    return true;
            }
            return false;
        }

        public static bool IsHarveyNearby(float maxDistance = 6f)
        {
            var harvey = Game1.getCharacterFromName("Harvey");
            if (harvey?.currentLocation != Game1.currentLocation) 
                return false;

            var harveyPos = harvey.getStandingPosition();
            var playerPos = Game1.player.getStandingPosition();
            return Microsoft.Xna.Framework.Vector2.Distance(harveyPos, playerPos) <= maxDistance * Game1.tileSize;
        }

        public static bool IsInRestZone()
        {
            var location = Game1.currentLocation;
            var locationName = location?.NameOrUniqueName;
            
            return locationName == "FarmHouse" 
                || locationName == "Hospital" 
                || locationName == "HarveyRoom"
                || location is StardewValley.Locations.BathHousePool;
        }

        public static bool IsInWarmZone()
        {
            var location = Game1.currentLocation;
            var locationName = location?.NameOrUniqueName;
            
            return locationName == "FarmHouse" 
                || locationName == "Hospital" 
                || location is StardewValley.Locations.BathHousePool;
        }
    }
}

