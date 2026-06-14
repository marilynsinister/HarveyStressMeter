using HarveyStressMeter.Models;
using HarveyStressMeter.Services;
using HarveyStressMeter.UI;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace HarveyStressMeter.Handlers
{
    /// <summary>
    /// Handles UI elements: Harvey panel, HUD messages, menu interactions
    /// Follows Single Responsibility Principle - only UI concerns
    /// </summary>
    public class UIHandler
    {
        private readonly IMonitor _monitor;
        private readonly SaveData _data;
        private readonly IModHelper _helper;
        private readonly ModConfig _config;
        private readonly HarveyPanelMenu _harveyPanelMenu;
        private readonly HarveyPanelService _harveyPanelService;

        public UIHandler(
            IMonitor monitor,
            SaveData data,
            IModHelper helper,
            ModConfig config,
            HarveyPanelMenu harveyPanelMenu,
            HarveyPanelService harveyPanelService)
        {
            _monitor = monitor;
            _data = data;
            _helper = helper;
            _config = config;
            _harveyPanelMenu = harveyPanelMenu;
            _harveyPanelService = harveyPanelService;
        }

        public void Initialize()
        {
            _harveyPanelMenu.TryInitialize(_helper);

            if (_harveyPanelMenu.IsAvailable)
                _monitor.Log("[HarveyPanel] Окно «План Харви» готово (клавиша из OpenHandbook).", LogLevel.Info);
            else
                _monitor.Log("[HarveyPanel] StardewUI недоступен — окно «План Харви» не откроется.", LogLevel.Warn);
        }

        public void HandleRenderedActiveMenu(RenderedActiveMenuEventArgs e)
        {
        }

        public bool HandleButtonPressed(ButtonPressedEventArgs e)
        {
            if (!_config.OpenHandbook.JustPressed())
                return false;

            if (_harveyPanelMenu.IsOpen)
            {
                _harveyPanelMenu.Close();
                _helper.Input.SuppressActiveKeybinds(_config.OpenHandbook);
            }

            return false;
        }

        public void HandleButtonsChanged(ButtonsChangedEventArgs e)
        {
            if (!_config.OpenHandbook.JustPressed())
                return;

            if (_harveyPanelMenu.IsOpen)
                return;

            if (!Context.IsPlayerFree)
                return;

            ToggleHarveyPanel();
            _helper.Input.SuppressActiveKeybinds(_config.OpenHandbook);
        }

        public void OpenHandbook()
        {
            OpenPanel(HarveyPanelTab.Overview);
        }

        public void OpenPanel(HarveyPanelTab tab)
        {
            if (!Context.IsWorldReady)
                return;

            if (_harveyPanelMenu.IsOpen)
            {
                _harveyPanelMenu.OpenToTab(_harveyPanelService, tab);
                return;
            }

            _harveyPanelMenu.TryOpen(_harveyPanelService, tab);
        }

        private void ToggleHarveyPanel()
        {
            _harveyPanelMenu.Toggle(_harveyPanelService);
        }
    }
}
