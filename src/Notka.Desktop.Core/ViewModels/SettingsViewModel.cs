using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Notka.Desktop.Services;

namespace Notka.Desktop.ViewModels
{
    public interface IAppLifetime
    {
        void RequestExit();
    }

    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IAutostart _autostart;
        private readonly IAppLifetime _lifetime;
        private bool _applying;

        public SettingsViewModel(IAutostart autostart, IAppLifetime lifetime)
        {
            _autostart = autostart;
            _lifetime = lifetime;
            _startWithWindows = autostart.IsEnabled;
        }

        [ObservableProperty]
        private bool _isOpen;

        [ObservableProperty]
        private bool _startWithWindows;

        [ObservableProperty]
        private string? _error;

        [RelayCommand]
        private void Toggle() => IsOpen = !IsOpen;

        [RelayCommand]
        private void Close() => IsOpen = false;

        [RelayCommand]
        private void Exit() => _lifetime.RequestExit();

        partial void OnStartWithWindowsChanged(bool value)
        {
            if (_applying)
            {
                return;
            }

            try
            {
                if (value)
                {
                    _autostart.Enable();
                }
                else
                {
                    _autostart.Disable();
                }

                Error = null;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                Error = "Nie udało się zmienić uruchamiania z Windows.";
                _applying = true;
                StartWithWindows = _autostart.IsEnabled;
                _applying = false;
            }
        }
    }
}
