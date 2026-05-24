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
        private static Action? _onFoodConsumed;

        public static void Initialize(IMonitor monitor, Action onFoodConsumed)
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
        /// Сохраняет, было ли это реальное поедание локального игрока.
        /// </summary>
        public static void DoneEatingPrefix(Farmer __instance, ref bool __state)
        {
            __state = __instance.IsLocalPlayer
                && __instance.mostRecentlyGrabbedItem != null
                && __instance.itemToEat is StardewValley.Object o
                && o.Edibility != -300;
        }

        public static void DoneEatingPostfix(Farmer __instance, bool __state)
        {
            if (!__state)
                return;

            try
            {
                var consumed = __instance.itemToEat as StardewValley.Object;
                _monitor?.Log($"[FoodConsumptionPatch] ✅ Игрок съел: {consumed?.DisplayName ?? consumed?.Name}", LogLevel.Debug);
                _onFoodConsumed?.Invoke();
            }
            catch (Exception ex)
            {
                _monitor?.Log($"[FoodConsumptionPatch] ❌ Ошибка в патче: {ex.Message}\n{ex.StackTrace}", LogLevel.Error);
            }
        }
    }
}
