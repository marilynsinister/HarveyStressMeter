using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HarveyStressMeter.Models
{
    /// <summary>View model окна «План Харви» (StardewUI).</summary>
    public sealed class HarveyPanelViewModel : INotifyPropertyChanged
    {
        private string _selectedTabKey = nameof(HarveyPanelTab.Overview);

        public ObservableCollection<HarveyPanelTabButtonViewModel> Tabs { get; } = new();

        public string SelectedTabKey
        {
            get => _selectedTabKey;
            private set => SetField(ref _selectedTabKey, value);
        }

        public string OverviewStateLine { get; init; } = "";
        public string OverviewAssignmentLine { get; init; } = "";
        public string OverviewProgressLine { get; init; } = "";
        public string OverviewAfterLine { get; init; } = "";
        public string OverviewStressLine { get; init; } = "";
        public string OverviewInjuriesLine { get; init; } = "";
        public string OverviewAdviceLine { get; init; } = "";

        public string StressAssignmentTitle { get; init; } = "";
        public string StressAssignmentProgress { get; init; } = "";
        public string StressAssignmentObjective { get; init; } = "";
        public string StressAssignmentAfter { get; init; } = "";
        public string StressNoAssignmentLine { get; init; } = "";

        public HandbookViewModel Handbook { get; init; } = new();

        public string InjuriesBody { get; init; } = "";

        public string PlanTitle { get; init; } = "";
        public string PlanBody { get; init; } = "";

        public string TrustLevelLine { get; init; } = "";
        public string TrustDescriptionLine { get; init; } = "";
        public string TrustPermissionsLine { get; init; } = "";
        public string TrustPlaceholder { get; init; } = "";

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool SelectTab(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return true;

            SelectedTabKey = key;

            foreach (var tab in Tabs)
                tab.Active = string.Equals(tab.Key, key, StringComparison.Ordinal);

            return true;
        }

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
                return;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
