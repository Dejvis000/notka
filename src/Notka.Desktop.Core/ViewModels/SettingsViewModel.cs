using System.Collections.ObjectModel;
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
        private readonly IMonitorLister? _monitors;
        private string _stripTarget;
        private bool _applying;

        public SettingsViewModel(IAutostart autostart, IAppLifetime lifetime, AppSettings settings, IMonitorLister? monitors = null)
        {
            _autostart = autostart;
            _lifetime = lifetime;
            _settings = settings;
            _monitors = monitors;
            _startWithWindows = autostart.IsEnabled;
            _showStrip = settings.ShowStrip;
            _stripTarget = settings.StripTarget;
            _stripBackground = settings.StripBackground;
            _stripStatus = StatusText(settings.ShowStrip, StripState.Starting);
            RefreshStripTargets();
        }

        public event Action<bool>? ShowStripChanged;

        public event Action<string>? StripTargetChanged;

        public event Action<bool>? StripBackgroundChanged;

        [ObservableProperty]
        private bool _stripBackground;

        public ObservableCollection<StripTargetOption> StripTargetOptions { get; } = [];

        public string StripTarget
        {
            get => _stripTarget;
            set
            {
                if (value is null || value == _stripTarget)
                {
                    return;
                }

                _stripTarget = value;
                OnPropertyChanged();
                _settings.StripTarget = value;
                _settings.Save();
                StripTargetChanged?.Invoke(value);
            }
        }

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
        private void Toggle()
        {
            IsOpen = !IsOpen;
            if (IsOpen)
            {
                RefreshStripTargets();
            }
        }

        public void RefreshStripTargets()
        {
            var options = new List<StripTargetOption>
            {
                new(StripTargets.Primary, "Główny monitor"),
                new(StripTargets.All, "Wszystkie monitory"),
            };
            options.AddRange(_monitors?.List() ?? []);
            if (options.All(o => o.Id != StripTarget))
            {
                options.Add(new StripTargetOption(StripTarget, "Odłączony monitor (pasek na głównym)"));
            }

            StripTargetOptions.Clear();
            foreach (var option in options)
            {
                StripTargetOptions.Add(option);
            }

            OnPropertyChanged(nameof(StripTarget));
        }

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

        partial void OnStripBackgroundChanged(bool value)
        {
            _settings.StripBackground = value;
            _settings.Save();
            StripBackgroundChanged?.Invoke(value);
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
