using System;
using Microsoft.Xna.Framework;
using StardewValley;

namespace HarveyStressMeter.Helpers
{
    /// <summary>Утилиты для определения клика по NPC Харви (в т.ч. spouse в FarmHouse).</summary>
    public static class HarveyHelper
    {
        public static bool IsHarvey(NPC? npc)
            => npc?.Name?.Equals("Harvey", StringComparison.OrdinalIgnoreCase) == true;

        public static NPC? GetHarveyAtTile(GameLocation location, Vector2 tile)
        {
            var npc = location.isCharacterAtTile(tile);
            return IsHarvey(npc) ? npc : null;
        }

        /// <summary>
        /// Надёжный поиск Харви при взаимодействии: cursor grab → player grab → nearby fallback.
        /// </summary>
        public static NPC? TryGetInteractedHarvey(
            GameLocation location,
            Vector2 cursorGrabTile,
            bool lenientDistance = false)
        {
            var npc = GetHarveyAtTile(location, cursorGrabTile);
            if (npc != null)
                return npc;

            var playerGrabTile = Game1.player.GetGrabTile();
            npc = GetHarveyAtTile(location, playerGrabTile);
            if (npc != null)
                return npc;

            float maxDistance = lenientDistance ? 3f : 2f;
            NPC? nearest = null;
            var nearestDist = float.MaxValue;

            foreach (var character in location.characters)
            {
                if (!IsHarvey(character))
                    continue;

                var distance = Vector2.Distance(character.Tile, Game1.player.Tile);
                if (distance <= maxDistance && distance < nearestDist)
                {
                    nearest = character;
                    nearestDist = distance;
                }
            }

            return nearest;
        }

        public static NPC? FindHarveyInLocation(GameLocation location)
        {
            foreach (var npc in location.characters)
            {
                if (IsHarvey(npc))
                    return npc;
            }

            return null;
        }
    }
}
