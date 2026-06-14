namespace HarveyStressMeter.Api
{
    /// <summary>Host API общего окна «План Харви» для других модов Harvey Overhaul.</summary>
    public interface IHarveyPanelHostApi
    {
        void OpenPanel(string tabId);

        bool IsPanelOpen();
    }
}
