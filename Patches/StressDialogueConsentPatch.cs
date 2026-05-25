using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using HarveyStressMeter.Services;

namespace HarveyStressMeter.Patches
{
    /// <summary>
    /// Перехватывает выбор ответа в programmatic stress-диалоге (#$y quick response).
    /// </summary>
    public static class StressDialogueConsentPatch
    {
        private static IMonitor? _monitor;
        private static StressDialogueService? _stressDialogueService;

        public static void Initialize(IMonitor monitor, StressDialogueService stressDialogueService)
        {
            _monitor = monitor;
            _stressDialogueService = stressDialogueService;
        }

        public static void Apply(Harmony harmony)
        {
            var method = AccessTools.Method(typeof(Dialogue), nameof(Dialogue.chooseResponse));
            if (method == null)
            {
                _monitor?.Log("[StressDialogueConsentPatch] ❌ Dialogue.chooseResponse не найден", LogLevel.Error);
                return;
            }

            harmony.Patch(
                method,
                postfix: new HarmonyMethod(typeof(StressDialogueConsentPatch), nameof(ChooseResponsePostfix))
            );

            _monitor?.Log("[StressDialogueConsentPatch] ✅ Патч Dialogue.chooseResponse применён", LogLevel.Info);
        }

        public static void ChooseResponsePostfix(Response response, ref bool __result)
        {
            if (!__result || _stressDialogueService == null)
                return;

            if (_stressDialogueService.TryRecordConsentResponse(response.responseKey))
            {
                _monitor?.Log(
                    $"[StressDialogueConsentPatch] Записан выбор согласия: {response.responseKey}",
                    LogLevel.Debug);
            }
        }
    }
}
