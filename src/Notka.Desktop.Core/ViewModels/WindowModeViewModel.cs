using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Notka.Desktop.Services;

namespace Notka.Desktop.ViewModels
{
    public partial class WindowModeViewModel : ObservableObject
    {
        public const int DefaultNormalWidth = 1180;
        public const int DefaultNormalHeight = 760;
        public const int DefaultCompactWidth = 380;
        public const int DefaultCompactHeight = 640;
        public const int MinimumSize = 200;

        private readonly AppSettings _settings;

        public WindowModeViewModel(AppSettings settings)
        {
            _settings = settings;
            _isCompact = settings.Compact;
            _alwaysOnTop = settings.AlwaysOnTop;
            _showTasks = settings.CompactShowsTasks;
        }

        public bool KeepOnTop => IsCompact && AlwaysOnTop;

        public bool ShowNotes => !ShowTasks;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowNotes))]
        private bool _showTasks;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(KeepOnTop))]
        private bool _isCompact;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(KeepOnTop))]
        private bool _alwaysOnTop;

        public (int Width, int Height) GetSize(bool compact)
        {
            var (width, height, defaultWidth, defaultHeight) = compact
                ? (_settings.CompactWidth, _settings.CompactHeight, DefaultCompactWidth, DefaultCompactHeight)
                : (_settings.NormalWidth, _settings.NormalHeight, DefaultNormalWidth, DefaultNormalHeight);
            return width >= MinimumSize && height >= MinimumSize ? (width, height) : (defaultWidth, defaultHeight);
        }

        public void SetSize(bool compact, int width, int height)
        {
            if (width < MinimumSize || height < MinimumSize)
            {
                return;
            }

            if (compact)
            {
                _settings.CompactWidth = width;
                _settings.CompactHeight = height;
            }
            else
            {
                _settings.NormalWidth = width;
                _settings.NormalHeight = height;
            }

            _settings.Save();
        }

        [RelayCommand]
        private void ToggleCompact() => IsCompact = !IsCompact;

        [RelayCommand]
        private void ToggleAlwaysOnTop() => AlwaysOnTop = !AlwaysOnTop;

        partial void OnIsCompactChanged(bool value)
        {
            _settings.Compact = value;
            _settings.Save();
        }

        partial void OnShowTasksChanged(bool value)
        {
            _settings.CompactShowsTasks = value;
            _settings.Save();
        }

        partial void OnAlwaysOnTopChanged(bool value)
        {
            _settings.AlwaysOnTop = value;
            _settings.Save();
        }
    }
}
