using HarveyOverhaul.Core.Api;
using StardewModdingAPI;
using StardewValley;

namespace HarveyStressMeter.Helpers;

/// <summary>Делегирует проверку секундных таймеров в HarveyOverhaul.Core.</summary>
internal static class CoreTreatmentTimeGate
{
    private const string CoreModId = "marilynsinister.HarveyOverhaul.Core";
    private static IHarveyCoreApi? _coreApi;

    public static void Bind(IModHelper helper)
    {
        _coreApi = helper.ModRegistry.GetApi<IHarveyCoreApi>(CoreModId);
    }

    public static bool ShouldCountTreatmentTime()
        => _coreApi?.ShouldCountTreatmentTime() ?? LocalFallback();

    private static bool LocalFallback()
    {
        if (!Context.IsWorldReady)
            return false;

        if (Game1.activeClickableMenu != null)
            return false;

        if (GameStateHelper.IsEventActive())
            return false;

        if (Game1.dialogueUp)
            return false;

        if (Game1.paused)
            return false;

        return true;
    }
}
