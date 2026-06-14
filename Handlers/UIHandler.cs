using HarveyOverhaul.Core.Api;
using HarveyOverhaul.Core.Models;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace HarveyStressMeter.Handlers;

/// <summary>UI helpers — hotkey H перенесён в HarveyOverhaul.Core.</summary>
public class UIHandler
{
    private const string CoreModId = "marilynsinister.HarveyOverhaul.Core";

    private readonly IMonitor _monitor;
    private readonly IModHelper _helper;

    public UIHandler(IMonitor monitor, IModHelper helper)
    {
        _monitor = monitor;
        _helper = helper;
    }

    public void Initialize()
    {
    }

    public void HandleRenderedActiveMenu(RenderedActiveMenuEventArgs e)
    {
    }

    public bool HandleButtonPressed(ButtonPressedEventArgs e)
        => false;

    public void OpenHandbook()
    {
        var api = _helper.ModRegistry.GetApi<IHarveyCoreApi>(CoreModId);
        if (api == null)
        {
            _monitor.Log("[HarveyStressMeter] HarveyOverhaul.Core API not found — cannot open panel.", LogLevel.Warn);
            return;
        }

        api.OpenPanel(HarveyPanelTab.Overview);
    }
}
