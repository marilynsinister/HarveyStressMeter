using HarmonyLib;
using StardewValley;

namespace HarveyStressMeter.Patches
{
    /// <summary>
    /// Перехват checkAction у Харви для programmatic quest follow-up (приоритет над vanilla / Custom Kisses).
    /// </summary>
    internal static class HarveyInteractionPatch
    {
        private static System.Func<NPC, Farmer, GameLocation, bool>? _tryInterceptHarvey;

        public static void Initialize(System.Func<NPC, Farmer, GameLocation, bool> tryInterceptHarvey)
            => _tryInterceptHarvey = tryInterceptHarvey;

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(
                original: AccessTools.Method(typeof(NPC), nameof(NPC.checkAction)),
                prefix: new HarmonyMethod(typeof(HarveyInteractionPatch), nameof(CheckActionPrefix)));
        }

        private static bool CheckActionPrefix(NPC __instance, Farmer who, GameLocation l, ref bool __result)
        {
            if (_tryInterceptHarvey == null || __instance.Name != "Harvey")
                return true;

            if (_tryInterceptHarvey(__instance, who, l))
            {
                __result = true;
                return false;
            }

            return true;
        }
    }
}
