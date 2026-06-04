using System;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Monsters;
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

        /// <summary>ID текущего event script (global или location-bound), если есть.</summary>
        public static string? GetCurrentEventId()
        {
            if (Game1.CurrentEvent?.id is { Length: > 0 } globalId)
                return globalId;

            if (Game1.currentLocation?.currentEvent?.id is { Length: > 0 } locationId)
                return locationId;

            return null;
        }

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

        public static bool TryGetHarveyDistanceTiles(out NPC? harvey, out float distanceTiles)
        {
            harvey = Game1.getCharacterFromName("Harvey");
            distanceTiles = -1f;

            if (harvey?.currentLocation != Game1.currentLocation)
            {
                harvey = null;
                return false;
            }

            var harveyPos = harvey.getStandingPosition();
            var playerPos = Game1.player.getStandingPosition();
            var pixelDistance = Vector2.Distance(harveyPos, playerPos);
            distanceTiles = pixelDistance / Math.Max(1f, Game1.tileSize);
            return true;
        }

        public static bool IsHarveyNearby(float maxDistanceTiles = 6f)
            => TryGetHarveyDistanceTiles(out _, out var distance)
               && distance <= maxDistanceTiles;

        public static bool IsDangerousStressLocation()
        {
            var name = Game1.currentLocation?.NameOrUniqueName ?? "";
            if (string.IsNullOrEmpty(name))
                return false;

            return name.Contains("Mine", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("Skull", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("Volcano", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("Dungeon", StringComparison.OrdinalIgnoreCase)
                   || name.Equals("UndergroundMine", StringComparison.OrdinalIgnoreCase);
        }

        public static bool HasHostileMonstersNearby(float rangeTiles = 8f)
        {
            var location = Game1.currentLocation;
            if (location == null)
                return false;

            var playerTile = Game1.player.Tile;
            foreach (var character in location.characters)
            {
                if (character is not Monster monster || monster.Health <= 0)
                    continue;

                if (Vector2.Distance(monster.Tile, playerTile) <= rangeTiles)
                    return true;
            }

            return false;
        }

        public static bool IsSafeAuraBlockedContext()
        {
            if (IsEventActive())
                return true;

            try
            {
                if (Game1.isFestival())
                    return true;
            }
            catch
            {
                // ignored
            }

            if (IsDangerousStressLocation())
                return true;

            if (HasHostileMonstersNearby())
                return true;

            return false;
        }

        public static bool IsClinicLocation()
            => IsLocationOneOf(Game1.currentLocation, "Hospital");

        public static bool IsMarriedFarmHouseContext()
            => HarveyFriendshipHelper.IsMarriedToHarvey()
               && IsLocationOneOf(Game1.currentLocation, "FarmHouse");

        /// <summary>Дом игрока: фермерский дом, островной дом, хижина (мультиплеер).</summary>
        public static bool IsPlayerHomeLocation(GameLocation? location = null)
        {
            location ??= Game1.player?.currentLocation;
            if (location == null)
                return false;

            if (location is StardewValley.Locations.FarmHouse)
                return true;

            var name = location.NameOrUniqueName;
            return name is "FarmHouse" or "IslandFarmHouse" or "Cabin";
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

        public static bool IsOutdoors()
            => Game1.currentLocation?.IsOutdoors == true;

        public static bool IsIndoors()
            => Game1.currentLocation != null && !Game1.currentLocation.IsOutdoors;

        public static bool IsTownLocation()
            => IsLocationOneOf(Game1.currentLocation, "Town");

        /// <summary>Лесные локации, где гром приглушён деревьями.</summary>
        public static bool IsForestShelterLocation()
            => IsLocationOneOf(Game1.currentLocation, "Forest", "Woods", "SecretWoods");

        public static bool IsOptionalForestShelterLocation()
            => IsForestShelterLocation() || IsLocationOneOf(Game1.currentLocation, "FarmCave");

        /// <summary>Тихие/безопасные локации для квеста AnxietySpike.</summary>
        public static bool IsAnxietySafeLocation()
            => IsInRestZone()
               || IsForestShelterLocation()
               || IsMarriedFarmHouseContext();

        public static bool IsStressfulWorkLocation(string? locationName)
        {
            if (string.IsNullOrEmpty(locationName))
                return false;

            return locationName is "Mine" or "SkullCave" or "VolcanoDungeon" or "Caldera";
        }

        public static bool IsBlockingFlashbackContext()
        {
            if (IsEventActive())
                return true;

            if (Game1.dialogueUp)
                return true;

            if (Game1.activeClickableMenu != null)
                return true;

            try
            {
                if (Game1.isFestival())
                    return true;
            }
            catch
            {
                // ignored
            }

            var locationName = Game1.currentLocation?.NameOrUniqueName ?? "";
            if (locationName.Contains("Festival", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        public static bool IsStormWeather()
            => Game1.isLightning;

        /// <summary>
        /// True when a real dialogue/menu blocks MCP environment tools (ignores stale dialogueUp).
        /// </summary>
        public static bool IsEnvironmentDialogueBlocking()
        {
            if (Game1.activeClickableMenu != null)
                return true;

            if (!Game1.dialogueUp)
                return false;

            var speaker = Game1.currentSpeaker;
            return speaker?.CurrentDialogue is { Count: > 0 };
        }

        /// <summary>Force-close menus opened by MCP/event tests.</summary>
        public static void ForceCloseActiveMenu()
        {
            for (var i = 0; i < 8 && Game1.activeClickableMenu != null; i++)
            {
                try
                {
                    Game1.exitActiveMenu();
                }
                catch
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Clears ghost dialogueUp/eventUp and restores movement after MCP/event cleanup.
        /// </summary>
        public static void ClearStaleUiFlags()
        {
            if (Game1.activeClickableMenu == null)
            {
                Game1.dialogueUp = false;

                if (!IsEventActive())
                    Game1.currentSpeaker = null;
            }

            if (!IsEventActive())
                Game1.eventUp = false;

            try
            {
                Game1.player?.forceCanMove();
            }
            catch
            {
                // ignored
            }
        }

        /// <summary>Clears fade/warp flags left by performWarpFarmer / debug warps.</summary>
        public static void ForceClearFadeAndWarpFlags()
        {
            Game1.locationRequest = null;
            Game1.fadeClear();
            Game1.globalFade = false;

            try
            {
                Game1.globalFadeToClear();
            }
            catch
            {
                // ignored
            }

            Game1.globalFade = false;
        }
    }
}

