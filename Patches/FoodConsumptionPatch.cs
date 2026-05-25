using HarmonyLib;
using StardewValley;
using StardewModdingAPI;
using System;

namespace HarveyStressMeter.Patches
{
    /// <summary>
    /// Harmony-патч для отслеживания потребления еды.
    /// Патчит Farmer.doneEating — вызывается после завершения анимации поедания/питья.
    /// </summary>
    public static class FoodConsumptionPatch
    {
        private static IMonitor? _monitor;
        private static Action<StardewValley.Object>? _onFoodConsumed;

        public static void Initialize(IMonitor monitor, Action<StardewValley.Object> onFoodConsumed)
        {
            _monitor = monitor;
            _onFoodConsumed = onFoodConsumed;
        }

        public static void Apply(Harmony harmony)
        {
            var method = AccessTools.Method(typeof(Farmer), nameof(Farmer.doneEating));
            if (method == null)
            {
                _monitor?.Log("[FoodConsumptionPatch] ❌ Метод Farmer.doneEating не найден", LogLevel.Error);
                return;
            }

            harmony.Patch(
                method,
                prefix: new HarmonyMethod(typeof(FoodConsumptionPatch), nameof(DoneEatingPrefix)),
                postfix: new HarmonyMethod(typeof(FoodConsumptionPatch), nameof(DoneEatingPostfix))
            );

            _monitor?.Log("[FoodConsumptionPatch] ✅ Патч Farmer.doneEating применён", LogLevel.Info);
        }

        /// <summary>
        /// Сохраняет съеденный предмет для postfix (до очистки itemToEat).
        /// </summary>
        public static void DoneEatingPrefix(Farmer __instance, ref StardewValley.Object? __state)
        {
            __state = null;

            if (!__instance.IsLocalPlayer)
                return;

            if (__instance.mostRecentlyGrabbedItem == null)
                return;

            if (__instance.itemToEat is not StardewValley.Object consumed || consumed.Edibility == -300)
                return;

            __state = consumed;
        }

        public static void DoneEatingPostfix(Farmer __instance, StardewValley.Object? __state)
        {
            if (__state == null)
                return;

            try
            {
                _monitor?.Log(
                    $"[FoodConsumptionPatch] ✅ Игрок съел: {__state.DisplayName ?? __state.Name} ({__state.QualifiedItemId})",
                    LogLevel.Debug);
                _onFoodConsumed?.Invoke(__state);
            }
            catch (Exception ex)
            {
                _monitor?.Log($"[FoodConsumptionPatch] ❌ Ошибка в патче: {ex.Message}\n{ex.StackTrace}", LogLevel.Error);
            }
        }
    }
}
