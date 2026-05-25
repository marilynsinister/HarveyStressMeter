using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using HarveyStressMeter.Models;

namespace HarveyStressMeter.Handlers
{
    /// <summary>
    /// Handles UI elements: handbook, HUD messages, menu interactions
    /// Follows Single Responsibility Principle - only UI concerns
    /// </summary>
    public class UIHandler
    {
        private readonly IMonitor _monitor;
        private readonly SaveData _data;
        private readonly IModHelper _helper;

        public UIHandler(IMonitor monitor, SaveData data, IModHelper helper)
        {
            _monitor = monitor;
            _data = data;
            _helper = helper;
        }

        public void Initialize()
        {
            // StardewUI / handbook textures disabled — re-add fields and menu hooks when re-enabled.
            _monitor.Log("Справочник временно отключён (StardewUI закомментирован)", LogLevel.Info);
        }

        public void HandleRenderedActiveMenu(RenderedActiveMenuEventArgs e)
        {
            // Handbook UI inactive while StardewUI is disabled.
        }

        public void HandleButtonPressed(ButtonPressedEventArgs e)
        {
            // Handbook UI inactive while StardewUI is disabled.
        }

        public void HandleButtonsChanged(ButtonsChangedEventArgs e)
        {
            if (!Context.IsPlayerFree) return;

            // TODO: Add config for open handbook key
            // if (_config.OpenHandbook.JustPressed())
            // {
            //     OpenHandbook();
            //     Helper.Input.SuppressActiveKeybinds(_config.OpenHandbook);
            // }
        }

        public void OpenHandbook()
        {
            _monitor.Log("Справочник временно отключён", LogLevel.Info);
        }
    }
}
