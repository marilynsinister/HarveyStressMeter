using System;
using System.IO;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;
using StardewModdingAPI;
using StardewUI.Framework;
using StardewValley;

namespace HarveyStressMeter.UI
{
    /// <summary>StardewUI-окно «План Харви» с вкладками.</summary>
    public sealed class HarveyPanelMenu
    {
        public const string ModUniqueId = "marilynsinister.HarveyStressMeter";
        public const string ViewAssetName = "Mods/marilynsinister.HarveyStressMeter/Views/HarveyPanel";

        private readonly IMonitor _monitor;
        private IViewEngine? _viewEngine;
        private IMenuController? _menuController;
        private HarveyPanelViewModel? _activeViewModel;
        private HarveyPanelTab _lastTab = HarveyPanelTab.Overview;
        private bool _assetsRegistered;

        public HarveyPanelMenu(IMonitor monitor)
        {
            _monitor = monitor;
        }

        public bool IsAvailable => _viewEngine != null;

        public bool IsOpen =>
            _menuController?.Menu != null
            && Game1.activeClickableMenu == _menuController.Menu;

        public void TryInitialize(IModHelper helper)
        {
            if (_viewEngine != null)
                return;

            if (!helper.ModRegistry.IsLoaded("focustense.StardewUI"))
            {
                _monitor.Log("[HarveyPanel] StardewUI не установлен — окно «План Харви» недоступно.", LogLevel.Warn);
                return;
            }

            _viewEngine = helper.ModRegistry.GetApi<IViewEngine>("focustense.StardewUI");
            if (_viewEngine == null)
            {
                _monitor.Log("[HarveyPanel] StardewUI API недоступен.", LogLevel.Warn);
                return;
            }

            string viewsDirectory = Path.Combine(helper.DirectoryPath, "assets", "views");
            string spritesDirectory = Path.Combine(helper.DirectoryPath, "assets", "sprites");
            _viewEngine.RegisterViews($"Mods/{ModUniqueId}/Views", viewsDirectory);
            _viewEngine.RegisterSprites($"Mods/{ModUniqueId}/Sprites", spritesDirectory);
            _viewEngine.PreloadModels(typeof(HarveyPanelViewModel), typeof(HarveyPanelTabButtonViewModel), typeof(HandbookViewModel), typeof(HandbookRow));
            _viewEngine.PreloadAssets();
            _assetsRegistered = true;

            _monitor.Log("[HarveyPanel] StardewUI views зарегистрированы.", LogLevel.Debug);
        }

        public void Toggle(HarveyPanelService panelService)
        {
            if (IsOpen)
            {
                Close();
                return;
            }

            TryOpen(panelService);
        }

        public void TryOpen(HarveyPanelService panelService, HarveyPanelTab? tab = null)
        {
            if (!Context.IsWorldReady)
                return;

            if (_viewEngine == null || !_assetsRegistered)
            {
                _monitor.Log("[HarveyPanel] StardewUI не инициализирован.", LogLevel.Warn);
                Game1.addHUDMessage(new HUDMessage("Окно «План Харви» недоступно (нужен StardewUI).", HUDMessage.error_type));
                return;
            }

            if (!Context.IsPlayerFree && !IsOpen)
            {
                _monitor.Log("[HarveyPanel] Игрок занят — окно не открыто.", LogLevel.Debug);
                return;
            }

            if (Game1.activeClickableMenu != null && !IsOpen)
            {
                _monitor.Log("[HarveyPanel] Уже открыто другое меню.", LogLevel.Debug);
                return;
            }

            var selectedTab = tab ?? _lastTab;
            HarveyPanelViewModel viewModel = panelService.BuildViewModel(selectedTab);
            _activeViewModel = viewModel;

            _menuController?.Dispose();
            _menuController = _viewEngine.CreateMenuControllerFromAsset(ViewAssetName, viewModel);
            _menuController.Closed += OnMenuClosed;
            Game1.activeClickableMenu = _menuController.Menu;

            _monitor.Log($"[HarveyPanel] Окно открыто, вкладка={selectedTab}.", LogLevel.Debug);
        }

        public void Close()
        {
            if (!IsOpen || _menuController == null)
                return;

            _menuController.Menu.exitThisMenu();
        }

        public void OpenToTab(HarveyPanelService panelService, HarveyPanelTab tab)
        {
            if (IsOpen)
                Close();

            TryOpen(panelService, tab);
        }

        private void OnMenuClosed()
        {
            if (_activeViewModel != null
                && Enum.TryParse<HarveyPanelTab>(_activeViewModel.SelectedTabKey, out var tab))
            {
                _lastTab = tab;
            }

            _activeViewModel = null;
            _menuController = null;
        }
    }
}
