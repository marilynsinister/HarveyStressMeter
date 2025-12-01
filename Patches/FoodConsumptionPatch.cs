using HarmonyLib;
using StardewValley;
using StardewModdingAPI;
using System;

namespace HarveyStressMeter.Patches
{
    /// <summary>
    /// Harmony патч для отслеживания потребления еды
    /// Патчит метод Farmer.eatObject для точного определения когда игрок съел еду
    /// </summary>
    [HarmonyPatch(typeof(Farmer), "eatObject")]
    public class FoodConsumptionPatch
    {
        private static IMonitor? _monitor;
        private static Action? _onFoodConsumed;

        /// <summary>
        /// Инициализирует патч с monitor и callback
        /// </summary>
        public static void Initialize(IMonitor monitor, Action onFoodConsumed)
        {
            _monitor = monitor;
            _onFoodConsumed = onFoodConsumed;
        }

        /// <summary>
        /// Postfix патч - вызывается ПОСЛЕ того как игрок съел еду
        /// </summary>
        public static void Postfix(Farmer __instance, StardewValley.Object o)
        {
            try
            {
                // Проверяем что это локальный игрок
                if (__instance != Game1.player)
                    return;

                // Проверяем что предмет съедобный (Edibility > 0)
                if (o.Edibility > 0)
                {
                    _monitor?.Log($"[FoodConsumptionPatch] ✅ Игрок съел: {o.Name} (Edibility: {o.Edibility})", LogLevel.Debug);
                    
                    // Вызываем callback для обработки потребления еды
                    _onFoodConsumed?.Invoke();
                }
            }
            catch (Exception ex)
            {
                _monitor?.Log($"[FoodConsumptionPatch] ❌ Ошибка в патче: {ex.Message}\n{ex.StackTrace}", LogLevel.Error);
            }
        }
    }
}

