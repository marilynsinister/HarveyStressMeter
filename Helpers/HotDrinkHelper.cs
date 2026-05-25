using System;
using System.Collections.Generic;
using StardewValley;

namespace HarveyStressMeter.Helpers
{
    /// <summary>
    /// C-08: определяет, подходит ли съеденный предмет для TooCold (горячий напиток).
    /// </summary>
    public static class HotDrinkHelper
    {
        private static readonly HashSet<string> VanillaHotDrinkIds = new(StringComparer.OrdinalIgnoreCase)
        {
            "(O)395", // Coffee
            "(O)253", // Triple Shot Espresso
            "(O)614", // Green Tea
        };

        private static readonly HashSet<string> DeniedQualifiedIds = new(StringComparer.OrdinalIgnoreCase)
        {
            "(O)167", // Joja Cola
        };

        private static readonly string[] DenyContextTags =
        {
            "alcohol_item",
            "milk_item",
            "medicine_item",
            "potion_item",
            "juice_item",
            "food_soup",
        };

        public static bool IsHotDrinkOrWarmingFood(StardewValley.Object? item)
        {
            if (item == null)
                return false;

            var qualifiedId = GetQualifiedItemId(item);
            if (DeniedQualifiedIds.Contains(qualifiedId))
                return false;

            if (VanillaHotDrinkIds.Contains(qualifiedId))
                return true;

            if (HasAnyDenyTag(item))
                return false;

            if (item.HasContextTag("coffee_item") || item.HasContextTag("hot_drink_item"))
                return true;

            return MatchesNameFallback(item.Name);
        }

        private static string GetQualifiedItemId(StardewValley.Object item)
        {
            if (!string.IsNullOrEmpty(item.QualifiedItemId))
                return item.QualifiedItemId;

            if (!string.IsNullOrEmpty(item.ItemId))
                return $"(O){item.ItemId}";

            return $"(O){item.ParentSheetIndex}";
        }

        private static bool HasAnyDenyTag(StardewValley.Object item)
        {
            foreach (var tag in DenyContextTags)
            {
                if (item.HasContextTag(tag))
                    return true;
            }

            return false;
        }

        private static bool MatchesNameFallback(string? name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            if (name.Contains("Coffee", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Espresso", StringComparison.OrdinalIgnoreCase))
                return true;

            if (name.Equals("Green Tea", StringComparison.OrdinalIgnoreCase))
                return true;

            if (name.Contains("Tea", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("Stardrop", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }
    }
}
