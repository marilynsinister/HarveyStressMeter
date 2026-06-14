using System;
using Microsoft.Xna.Framework;
using StardewValley;

namespace HarveyStressMeter.Helpers
{
    /// <summary>Утилиты для определения клика по NPC Харви (в т.ч. spouse в FarmHouse).</summary>
    public static class HarveyHelper
    {
        public static NPC? GetHarveyAtTile(GameLocation location, Vector2 tile)
        {
            var npc = location.isCharacterAtTile(tile);
            return npc?.Name?.Equals("Harvey", StringComparison.OrdinalIgnoreCase) == true ? npc : null;
        }

        public static NPC? FindHarveyInLocation(GameLocation location)
        {
            foreach (var npc in location.characters)
            {
                if (npc.Name.Equals("Harvey", StringComparison.OrdinalIgnoreCase))
                    return npc;
            }

            return null;
        }
    }
}
