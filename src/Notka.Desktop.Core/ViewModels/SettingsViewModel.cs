using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Notka.Desktop.Services;
using Notka.Desktop.Taskbar;

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
        private readonly AppSettings _settings;
        private bool _applying;

        public SettingsViewModel(IAutostart autostart, IAppLifetime lifetime, AppSettings settings)
        {
            _autostart = autostart;
            _lifetime = lifetime;
            _settings = settings;
            _startWithWindows = autostart.IsEnabled;
            _showStrip = settings.ShowStrip;
            _stripStatus = StatusText(settings.ShowStrip, StripState.Starting);
        }

        public event Action<bool>? ShowStripChanged;

        [ObservableProperty]
        private bool _isOpen;

        [ObservableProperty]
        private bool _showStrip;

        [ObservableProperty]
        private string _stripStatus;

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

        public void ReportStripState(StripState state) => StripStatus = StatusText(ShowStrip, state);

        partial void OnShowStripChanged(bool value)
        {
            _settings.ShowStrip = value;
            _settings.Save();
            StripStatus = StatusText(value, StripState.Starting);
            ShowStripChanged?.Invoke(value);
        }

        private static string StatusText(bool enabled, StripState state) => !enabled
            ? "Pasek jest wyłączony."
            : state switch
            {
                StripState.Visible => "Pasek jest widoczny przy zegarze.",
                StripState.Unavailable => "Pasek jest niedostępny (brak miejsca lub zasłonięty pasek zadań). Informacja jest w podpowiedzi ikony w trayu.",
                _ => "Pasek się uruchamia.",
            };

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
