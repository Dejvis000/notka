using Microsoft.UI.Dispatching;
using Microsoft.Win32;
using Notka.Core;
using Notka.Desktop.Services;
using Notka.Desktop.ViewModels;
using Serilog;
using Windows.UI.ViewManagement;

namespace Notka.Desktop.Taskbar
{
    internal sealed class TaskbarStripController(
        TodaySummary summary,
        IChangesClient changes,
        SettingsViewModel settings,
        DispatcherQueue dispatcher,
        Action showMainWindow,
        Action<string> setTrayTooltip) : IDisposable
    {
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan ShowDelay = TimeSpan.FromMilliseconds(350);
        private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(450);

        private readonly TaskbarStripHost _host = new();
        private readonly UISettings _uiSettings = new();
        private readonly TodaySummary _summary = summary;
        private readonly IChangesClient _changes = changes;
        private readonly SettingsViewModel _settings = settings;
        private readonly DispatcherQueue _dispatcher = dispatcher;
        private readonly Action _showMainWindow = showMainWindow;
        private readonly Action<string> _setTrayTooltip = setTrayTooltip;
        private DispatcherQueueTimer? _timer;
        private DispatcherQueueTimer? _cardTimer;
        private StripCardWindow? _card;
        private StripContent _content = StripContent.Unavailable;
        private bool _stripHover;
        private bool _cardHover;
        private bool _disposed;
        private int _version;

        public void Start()
        {
            _host.Clicked += OnClicked;
            _host.HoverChanged += OnHoverChanged;
            _host.StateChanged += OnStateChanged;
            _changes.TaskChanged += OnTaskChanged;
            _settings.ShowStripChanged += _host.SetEnabled;
            _settings.StripTargetChanged += _host.SetTarget;
            _settings.StripBackgroundChanged += _host.SetBackground;
            _uiSettings.ColorValuesChanged += OnColorsChanged;

            _timer = _dispatcher.CreateTimer();
            _timer.Interval = RefreshInterval;
            _timer.Tick += OnTimerTick;
            _host.Start();
            _host.SetTarget(_settings.StripTarget);
            _host.SetBackground(_settings.StripBackground);
            _host.SetEnabled(_settings.ShowStrip);
            _timer.Start();
            _ = RefreshAsync();
        }

        public void Dispose()
        {
            _disposed = true;
            _host.Clicked -= OnClicked;
            _host.HoverChanged -= OnHoverChanged;
            _host.StateChanged -= OnStateChanged;
            _changes.TaskChanged -= OnTaskChanged;
            _settings.ShowStripChanged -= _host.SetEnabled;
            _settings.StripTargetChanged -= _host.SetTarget;
            _settings.StripBackgroundChanged -= _host.SetBackground;
            _uiSettings.ColorValuesChanged -= OnColorsChanged;
            _timer?.Stop();
            _cardTimer?.Stop();
            _card?.Close();
            _host.Dispose();
        }

        private static bool IsSystemThemeLight()
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }

        private void OnClicked() => _dispatcher.TryEnqueue(() => _showMainWindow());

        private void OnHoverChanged(bool hover) => _dispatcher.TryEnqueue(() => SetStripHover(hover));

        private void OnStateChanged(StripState state) => _dispatcher.TryEnqueue(() => _settings.ReportStripState(state));

        private void OnTaskChanged(ChangeEvent change) => _ = RefreshAsync();

        private void OnColorsChanged(UISettings sender, object args) => _dispatcher.TryEnqueue(() => _ = RefreshAsync());

        private void OnTimerTick(DispatcherQueueTimer sender, object args) => _ = RefreshAsync();

        private void SetStripHover(bool hover)
        {
            _stripHover = hover;
            ScheduleCardUpdate();
        }

        private void SetCardHover(bool hover)
        {
            _cardHover = hover;
            ScheduleCardUpdate();
        }

        private void ScheduleCardUpdate()
        {
            if (_disposed)
            {
                return;
            }

            _cardTimer ??= _dispatcher.CreateTimer();
            _cardTimer.Stop();
            _cardTimer.IsRepeating = false;
            _cardTimer.Interval = _stripHover || _cardHover ? ShowDelay : HideDelay;
            _cardTimer.Tick -= OnCardTimer;
            _cardTimer.Tick += OnCardTimer;
            _cardTimer.Start();
        }

        private void OnCardTimer(DispatcherQueueTimer sender, object args)
        {
            if (_disposed)
            {
                return;
            }

            if ((_stripHover || _cardHover) && _host.Bounds is { } strip)
            {
                if (_card is null)
                {
                    _card = new StripCardWindow();
                    _card.PointerOverChanged += SetCardHover;
                }

                _card.Show(strip, _content.CardLines, _host.Scale);
            }
            else
            {
                _card?.HideCard();
            }
        }

        private async Task RefreshAsync()
        {
            try
            {
                var version = ++_version;
                var content = await _summary.LoadAsync();
                if (_disposed || version != _version)
                {
                    return;
                }

                _content = content;
                _host.Update(content, IsSystemThemeLight());
                if (_card is not null && (_stripHover || _cardHover) && _host.Bounds is { } strip)
                {
                    _card.Show(strip, content.CardLines, _host.Scale);
                }

                var tooltip = content.Text.StartsWith("Notka:", StringComparison.Ordinal) ? content.Text : $"Notka: {content.Text}";
                _setTrayTooltip(tooltip.Length > 120 ? tooltip[..120] : tooltip);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not refresh the taskbar strip");
            }
        }
    }
}
