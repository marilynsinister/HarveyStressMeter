using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HarveyStressMeter.Models
{
    /// <summary>Кнопка вкладки для StardewUI.</summary>
    public sealed class HarveyPanelTabButtonViewModel : INotifyPropertyChanged
    {
        private bool _active;

        public string Key { get; init; } = "";

        public string Label { get; init; } = "";

        public bool Active
        {
            get => _active;
            set => SetField(ref _active, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
                return;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
