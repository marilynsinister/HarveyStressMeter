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
        public static bool IsTimeBetween(int from, int to) 
            => Game1.timeOfDay >= from && Game1.timeOfDay <= to;

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

