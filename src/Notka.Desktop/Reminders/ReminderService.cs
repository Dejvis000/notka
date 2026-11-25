using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Notka.Core;
using Notka.Desktop.Services;
using Notka.Desktop.ViewModels;
using Serilog;

namespace Notka.Desktop.Reminders
{
    internal sealed class ReminderService(
        INotkaApiClient api,
        IChangesClient changes,
        SettingsViewModel settings,
        DispatcherQueue dispatcher,
        Action showMainWindow) : IDisposable
    {
        private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

        private static readonly string StatePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notka", "reminders.json");

        private readonly INotkaApiClient _api = api;
        private readonly IChangesClient _changes = changes;
        private readonly SettingsViewModel _settings = settings;
        private readonly DispatcherQueue _dispatcher = dispatcher;
        private readonly Action _showMainWindow = showMainWindow;
        private ReminderState _state = new();
        private List<TaskItem> _tasks = [];
        private DispatcherQueueTimer? _timer;
        private string? _savedJson;
        private bool _registered;
        private bool _loaded;
        private bool _disposed;

        public void Start()
        {
            _state = LoadState();
            _changes.TaskChanged += OnTaskChanged;
            _changes.Reconnected += OnReconnected;
            try
            {
                AppNotificationManager.Default.NotificationInvoked += OnInvoked;
                AppNotificationManager.Default.Register(
                    "Notka",
                    new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "NotkaIcon.png")));
                _registered = true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "App notifications are unavailable; reminders are off");
                return;
            }

            _timer = _dispatcher.CreateTimer();
            _timer.Interval = TickInterval;
            _timer.Tick += (sender, args) =>
            {
                using var scope = Perf.Measure("reminders.tick");
                if (!_loaded)
                {
                    _ = ReloadAsync();
                }

                Evaluate();
            };
            _timer.Start();
            _ = ReloadAsync();
        }

        public void Dispose()
        {
            _disposed = true;
            _timer?.Stop();
            _changes.TaskChanged -= OnTaskChanged;
            _changes.Reconnected -= OnReconnected;
            if (_registered)
            {
                try
                {
                    AppNotificationManager.Default.NotificationInvoked -= OnInvoked;
                    AppNotificationManager.Default.Unregister();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Could not unregister app notifications");
                }
            }
        }

        private static ReminderState LoadState()
        {
            try
            {
                return File.Exists(StatePath) ? ReminderState.FromJson(File.ReadAllText(StatePath)) : new ReminderState();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new ReminderState();
            }
        }

        private void SaveState()
        {
            var json = _state.ToJson();
            if (json == _savedJson)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
                var temporary = StatePath + ".tmp";
                File.WriteAllText(temporary, json);
                File.Move(temporary, StatePath, overwrite: true);
                _savedJson = json;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warning(ex, "Could not save the reminder state");
            }
        }

        private void OnTaskChanged(ChangeEvent change) => _ = ReloadAsync();

        private void OnReconnected() => _ = ReloadAsync();

        private async Task ReloadAsync()
        {
            try
            {
                var tasks = await _api.ListTasksAsync("All");
                _dispatcher.TryEnqueue(() =>
                {
                    _tasks = tasks;
                    _loaded = true;
                    Evaluate();
                });
            }
            catch (Exception ex) when (ex is NotkaApiException or HttpRequestException or TaskCanceledException)
            {
                Log.Warning(ex, "Could not load tasks for reminders");
            }
        }

        private void Evaluate()
        {
            if (_disposed || !_registered || !_loaded || !_settings.Reminders)
            {
                return;
            }

            var toasts = ReminderEngine.Evaluate(DateTime.Now, _tasks, _state);
            SaveState();
            foreach (var toast in toasts)
            {
                Show(toast);
            }
        }

        private static void Show(ReminderToast toast)
        {
            try
            {
                var builder = new AppNotificationBuilder()
                    .AddArgument("action", "open")
                    .AddText(toast.Title)
                    .AddText(toast.Body);
                if (toast.TaskId is { } id)
                {
                    var taskId = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    builder
                        .AddButton(new AppNotificationButton("Zrobione").AddArgument("action", "done").AddArgument("id", taskId))
                        .AddButton(new AppNotificationButton("Za 15 min").AddArgument("action", "snooze").AddArgument("id", taskId));
                }

                AppNotificationManager.Default.Show(builder.BuildNotification());
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not show a reminder");
            }
        }

        private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
        {
            var action = args.Arguments.TryGetValue("action", out var a) ? a : "open";
            var id = args.Arguments.TryGetValue("id", out var i) && int.TryParse(i, out var parsed) ? parsed : (int?)null;
            _dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    switch (action)
                    {
                        case "done" when id is { } doneId:
                            await _api.CompleteTaskAsync(doneId);
                            await ReloadAsync();
                            break;
                        case "snooze" when id is { } snoozeId:
                            if (_tasks.FirstOrDefault(t => t.Id == snoozeId) is { } snoozed)
                            {
                                ReminderEngine.Snooze(DateTime.Now, snoozed, _state);
                            }

                            break;
                        default:
                            _showMainWindow();
                            break;
                    }
                }
                catch (Exception ex) when (ex is NotkaApiException or HttpRequestException or TaskCanceledException)
                {
                    Log.Warning(ex, "Reminder action {Action} failed", action);
                }
            });
        }
    }
}
