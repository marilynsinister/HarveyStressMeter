using HarveyStressMeter.Api;
using HarveyStressMeter.Models;
using HarveyStressMeter.Services;
using HarveyStressMeter.UI;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;

namespace HarveyStressMeter.Api
{
    /// <summary>Реализация host API для открытия общего окна «План Харви».</summary>
    public sealed class HarveyPanelHostApi : IHarveyPanelHostApi
    {
        private readonly HarveyPanelMenu _menu;
        private readonly HarveyPanelService _panelService;
        private readonly IMonitor _monitor;

        public HarveyPanelHostApi(
            HarveyPanelMenu menu,
            HarveyPanelService panelService,
            IMonitor monitor)
        {
            _menu = menu;
            _panelService = panelService;
            _monitor = monitor;
        }

        public bool IsPanelOpen() => _menu.IsOpen;

        public void OpenPanel(string tabId)
        {
            if (!Context.IsWorldReady)
                return;

            HarveyPanelTab? tab = ParseTabId(tabId);
            if (tab == null)
            {
                _monitor.Log($"[HarveyPanel] OpenPanel: неизвестная вкладка '{tabId}', открываю «Обзор».", LogLevel.Debug);
                tab = HarveyPanelTab.Overview;
            }

            _menu.OpenToTab(_panelService, tab.Value);
        }

        private static HarveyPanelTab? ParseTabId(string tabId)
        {
            if (string.IsNullOrWhiteSpace(tabId))
                return HarveyPanelTab.Overview;

            if (Enum.TryParse<HarveyPanelTab>(tabId, ignoreCase: true, out var parsed))
                return parsed;

            return tabId.Trim().ToLowerInvariant() switch
            {
                "overview" or "обзор" => HarveyPanelTab.Overview,
                "stress" or "стресс" => HarveyPanelTab.Stress,
                "injuries" or "injury" or "травмы" => HarveyPanelTab.Injuries,
                "plan" or "план" => HarveyPanelTab.Plan,
                "trust" or "доверие" => HarveyPanelTab.Trust,
                _ => null,
            };
        }
    }
}
