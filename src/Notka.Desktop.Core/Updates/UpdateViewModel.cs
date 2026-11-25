using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Notka.Desktop.Services;
using Notka.Desktop.ViewModels;

namespace Notka.Desktop.Updates
{
    public sealed record UpdateStep(string Text, double? Percent);

    public interface IUpdateInstaller
    {
        string? UnavailableReason { get; }

        Task StageAsync(ReleaseInfo release, IProgress<UpdateStep> progress, CancellationToken cancellationToken);

        void LaunchStaged();
    }

    public partial class UpdateViewModel : ObservableObject
    {
        private static readonly TimeSpan DefaultExitDelay = TimeSpan.FromSeconds(2);

        private readonly TimeSpan _exitDelay;
        private readonly IUpdateFeed _feed;
        private readonly IUpdateInstaller _installer;
        private readonly IAppLifetime _lifetime;
        private readonly AppSettings _settings;
        private readonly Version _current;
        private ReleaseInfo? _available;

        public UpdateViewModel(IUpdateFeed feed, IUpdateInstaller installer, IAppLifetime lifetime, AppSettings settings, Version current, TimeSpan? exitDelay = null)
        {
            _exitDelay = exitDelay ?? DefaultExitDelay;
            _feed = feed;
            _installer = installer;
            _lifetime = lifetime;
            _settings = settings;
            _current = current;
            _autoCheck = settings.AutoUpdateCheck;
        }

        public string CurrentVersionText => AppVersion.IsDev(_current)
            ? "Wersja deweloperska"
            : $"Wersja {AppVersion.Format(_current)}";

        public bool IsDevelopmentBuild => AppVersion.IsDev(_current);

        public string AvailableText => _available is null ? string.Empty : $"Dostępna wersja {AppVersion.Format(_available.Version)}";

        public bool HasUpdate => _available is not null;

        public bool CanInstall => HasUpdate && !IsBusy && _installer.UnavailableReason is null;

        public bool ShowBanner => HasUpdate || IsInstalling;

        public string BannerText => IsInstalling
            ? Status ?? string.Empty
            : _installer.UnavailableReason is { } reason && HasUpdate ? $"{AvailableText}. {reason}" : AvailableText;

        public bool ProgressIsIndeterminate => ProgressPercent is null;

        public double ProgressValue => ProgressPercent ?? 0;

        public string InstallLabel => _available is null ? "Zainstaluj" : $"Zainstaluj {AppVersion.Format(_available.Version)} i uruchom ponownie";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanInstall))]
        private bool _isBusy;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(BannerText))]
        private string? _status;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowBanner))]
        [NotifyPropertyChangedFor(nameof(BannerText))]
        private bool _isInstalling;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ProgressIsIndeterminate))]
        [NotifyPropertyChangedFor(nameof(ProgressValue))]
        private double? _progressPercent;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ResultMessage))]
        private string? _successMessage;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ResultMessage))]
        private string? _failureMessage;

        public string? ResultMessage => SuccessMessage ?? FailureMessage;

        [ObservableProperty]
        private bool _autoCheck;

        public void ShowResult(UpdateResult result)
        {
            SuccessMessage = result.Ok
                ? $"Notka została zaktualizowana do wersji {result.Version}. Jeśli używasz agentów AI, uruchom ponownie Claude Desktop."
                : null;
            FailureMessage = result.Ok
                ? null
                : !result.Attempted
                    ? "Aktualizacja nie została zastosowana, bo Notka nie zamknęła się na czas. Spróbuj ponownie."
                    : result.Restored
                        ? @"Aktualizacja się nie powiodła, przywrócono poprzednią wersję. Szczegóły: %LOCALAPPDATA%\Notka\update\update.log."
                        : @"Aktualizacja się nie powiodła i nie udało się w pełni przywrócić poprzedniej wersji. Pobierz Notkę ponownie z GitHub Releases. Szczegóły: %LOCALAPPDATA%\Notka\update\update.log.";
        }

        public async Task CheckAsync(bool manual)
        {
            if (IsBusy)
            {
                return;
            }

            IsBusy = true;
            try
            {
                var latest = await _feed.GetLatestAsync(CancellationToken.None);
                if (latest is not null && latest.Version > _current)
                {
                    SetAvailable(latest);
                    Status = _installer.UnavailableReason ?? AvailableText + ".";
                }
                else
                {
                    SetAvailable(null);
                    if (manual)
                    {
                        Status = latest is null
                            ? "Brak wydania z paczką aktualizacji w GitHub Releases."
                            : "Masz najnowszą wersję.";
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UpdateException)
            {
                if (manual)
                {
                    Status = "Nie udało się sprawdzić aktualizacji. Sprawdź połączenie z internetem.";
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private Task Check() => CheckAsync(manual: true);

        [RelayCommand]
        private async Task Install()
        {
            if (_available is not { } release || IsBusy)
            {
                return;
            }

            if (_installer.UnavailableReason is { } reason)
            {
                Status = reason;
                return;
            }

            IsBusy = true;
            IsInstalling = true;
            ProgressPercent = null;
            SuccessMessage = null;
            FailureMessage = null;
            Status = "Przygotowanie aktualizacji…";
            var exiting = false;
            try
            {
                var progress = new Progress<UpdateStep>(step =>
                {
                    Status = step.Text;
                    ProgressPercent = step.Percent;
                });
                await _installer.StageAsync(release, progress, CancellationToken.None);
                ProgressPercent = null;
                Status = "Notka zamknie się i za chwilę uruchomi się ponownie z nową wersją. Nie zamykaj komputera.";
                await Task.Delay(_exitDelay);
                exiting = await _lifetime.TryExitAsync();
                if (!exiting)
                {
                    FailureMessage = "Aktualizacja jest pobrana, ale Notka nie mogła się zamknąć (są niezapisane zmiany w notatkach). Zapisz je i spróbuj ponownie.";
                }
            }
            catch (UpdateException ex)
            {
                FailureMessage = ex.Message;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                FailureMessage = "Nie udało się zainstalować aktualizacji. Spróbuj ponownie.";
            }
            finally
            {
                if (!exiting)
                {
                    IsInstalling = false;
                    IsBusy = false;
                    Status = FailureMessage;
                }
            }
        }

        partial void OnAutoCheckChanged(bool value)
        {
            _settings.AutoUpdateCheck = value;
            _settings.Save();
        }

        private void SetAvailable(ReleaseInfo? release)
        {
            _available = release;
            OnPropertyChanged(nameof(HasUpdate));
            OnPropertyChanged(nameof(ShowBanner));
            OnPropertyChanged(nameof(BannerText));
            OnPropertyChanged(nameof(AvailableText));
            OnPropertyChanged(nameof(InstallLabel));
            OnPropertyChanged(nameof(CanInstall));
        }
    }
}
