using HarveyStressMeter.Helpers;
using HarveyStressMeter.Services;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace HarveyStressMeter.Handlers
{
    /// <summary>
    /// Перехват action-клика по Харви до vanilla/spouse dialogue.
    /// Приоритет: review → start/progress → vanilla.
    /// </summary>
    public sealed class HarveyStressInteractionHandler
    {
        private readonly IMonitor _monitor;
        private readonly IModHelper _helper;
        private readonly StressDialogueService _stressDialogueService;

        public HarveyStressInteractionHandler(
            IMonitor monitor,
            IModHelper helper,
            StressDialogueService stressDialogueService)
        {
            _monitor = monitor;
            _helper = helper;
            _stressDialogueService = stressDialogueService;
        }

        public bool TryHandleHarveyStressInteraction(ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady || !Context.IsPlayerFree)
                return false;

            if (GameStateHelper.IsEventActive())
                return false;

            if (Game1.activeClickableMenu is DialogueBox)
                return false;

            if (!e.Button.IsActionButton())
                return false;

            var harvey = TryGetClickedHarvey();
            if (harvey == null)
                return false;

            var locationName = Game1.currentLocation?.Name ?? "(null)";
            _monitor.Log($"[HarveyStress] Harvey clicked at {locationName}.", LogLevel.Info);

            if (_stressDialogueService.TryShowProgrammaticReviewDialogue(harvey))
            {
                SuppressHarveyClickButtons(e);
                _monitor.Log("[HarveyStress] Showing programmatic review before vanilla dialogue.", LogLevel.Info);
                return true;
            }

            if (_stressDialogueService.TryShowTreatmentStartOrProgressDialogue(harvey))
            {
                SuppressHarveyClickButtons(e);
                _monitor.Log("[HarveyStress] Showing programmatic treatment dialogue before vanilla dialogue.", LogLevel.Info);
                return true;
            }

            _monitor.Log("[HarveyStress] Harvey clicked, no pending review. Falling back to vanilla dialogue.", LogLevel.Debug);
            return false;
        }

        private NPC? TryGetClickedHarvey()
        {
            var location = Game1.currentLocation;
            if (location == null)
                return null;

            var tile = _helper.Input.GetCursorPosition().GrabTile;
            return HarveyHelper.GetHarveyAtTile(location, tile);
        }

        private void SuppressHarveyClickButtons(ButtonPressedEventArgs e)
        {
            _helper.Input.Suppress(e.Button);
            _helper.Input.Suppress(SButton.MouseLeft);
        }
    }
}
