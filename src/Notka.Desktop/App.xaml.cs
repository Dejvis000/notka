using System.Net.Sockets;
using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Notka.Desktop.Reminders;
using Notka.Desktop.Services;
using Notka.Desktop.Taskbar;
using Notka.Desktop.Updates;
using Notka.Desktop.ViewModels;
using Serilog;
using Windows.Graphics;

namespace Notka.Desktop
{
    public partial class App : Application, IAppLifetime
    {
        private readonly ApiHost _api = new();
        private DispatcherQueue? _dispatcher;
        private ServiceProvider? _ui;
        private MainViewModel? _viewModel;
        private MainWindow? _window;
        private TaskbarIcon? _tray;
        private TaskbarStripController? _strip;
        private bool _exiting;
        private DateTime? _exitWarnedAt;
        private bool _shutDown;
        private DispatcherQueueTimer[] _updateTimers = [];
        private IUpdateInstaller? _updateInstaller;
        private ReminderService? _reminders;

        public App()
        {
            InitializeComponent();
            UnhandledException += (_, e) =>
            {
                Log.Error(e.Exception, "Unhandled UI exception");
                e.Handled = true;
            };
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs args)
        {
            ConfigureLogging();
            _dispatcher = DispatcherQueue.GetForCurrentThread();

            try
            {
                await _api.StartAsync(AppSettings.Load(AppSettings.DefaultPath()).AllowAgents);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not start the API host");
                ShowStartupError(ex.GetBaseException() is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse }
                    ? "Nie udało się uruchomić Notki. Port jest zajęty przez inną aplikację. " +
                        "Ustaw inny port w zmiennej środowiskowej Notka__Port i uruchom aplikację ponownie."
                    : "Nie udało się uruchomić Notki. Uruchom aplikację ponownie.");
                return;
            }

            try
            {
                StartUi();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not start the user interface");
                _tray?.Dispose();
                await _api.StopAsync();
                ShowStartupError("Nie udało się uruchomić interfejsu Notki. Uruchom aplikację ponownie.");
            }
        }

        private void StartUi()
        {
            var exePath = Environment.ProcessPath ?? throw new InvalidOperationException("Process path is unavailable.");
            var ui = BuildUiServices(_api.Url, _api.Token, new AutostartService(exePath), this);
            _ui = ui;
            var settings = ui.GetRequiredService<SettingsViewModel>();
            _api.SetAgentAccess(settings.AllowAgents);
            settings.AllowAgentsChanged += _api.SetAgentAccess;
            _viewModel = ui.GetRequiredService<MainViewModel>();
            var updates = ui.GetRequiredService<UpdateViewModel>();
            _updateInstaller = ui.GetRequiredService<IUpdateInstaller>();
            ShowPreviousUpdateResult(updates);
            _window = new MainWindow(_viewModel, ui.GetRequiredService<SettingsViewModel>(), ui.GetRequiredService<WindowModeViewModel>(), updates);
            _window.Closed += async (_, _) => await ShutdownAsync();

            CreateTray();
            _strip = new TaskbarStripController(
                new TodaySummary(ui.GetRequiredService<INotkaApiClient>(), TimeProvider.System),
                ui.GetRequiredService<IChangesClient>(),
                ui.GetRequiredService<SettingsViewModel>(),
                _dispatcher!,
                ShowMainWindow,
                tooltip => _tray!.ToolTipText = tooltip);
            _strip.Start();
            _reminders = new ReminderService(
                ui.GetRequiredService<INotkaApiClient>(),
                ui.GetRequiredService<IChangesClient>(),
                settings,
                _dispatcher!,
                ShowMainWindow);
            _reminders.Start();
            _ = InitializeViewModelAsync();
            StartUpdateChecks(updates);
            var launchArguments = Environment.GetCommandLineArgs();
            var startHidden = launchArguments.Contains("-Embedding")
                || settings.StartMode == StartModes.Tray
                || (settings.StartMode == StartModes.Last && launchArguments.Contains(AutostartService.TrayArgument));
            if (!startHidden)
            {
                _window.Activate();
            }

            Program.ActivationHandler = () => _dispatcher?.TryEnqueue(ShowMainWindow);
        }

        public void RequestExit() => _ = ExitAsync();

        private async Task InitializeViewModelAsync()
        {
            try
            {
                await _viewModel!.InitializeAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not initialize the view models");
            }
        }

        private static ServiceProvider BuildUiServices(Uri apiUrl, string token, IAutostart autostart, IAppLifetime lifetime)
        {
            var services = new ServiceCollection();
            services.AddSingleton(autostart);
            services.AddSingleton(lifetime);
            services.AddSingleton(AppSettings.Load(AppSettings.DefaultPath()));
            services.AddSingleton<IMonitorLister, MonitorLister>();
            services.AddSingleton<IAgentConnector, AgentConnector>();
            AddUpdates(services);
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<WindowModeViewModel>();
            services.AddHttpClient<INotkaApiClient, NotkaApiClient>(client =>
            {
                client.BaseAddress = apiUrl;
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.Authorization = new("Bearer", token);
            });
            services.AddSingleton<IChangesClient>(_ => new ChangesClient(apiUrl, token));
            services.AddTransient<MainViewModel>();
            return services.BuildServiceProvider();
        }

        private static void AddUpdates(IServiceCollection services)
        {
            var current = AppVersion.Parse(typeof(App).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion) ?? AppVersion.Dev;
            var feedUrl = Uri.TryCreate(Environment.GetEnvironmentVariable("NOTKA_UPDATE_FEED"), UriKind.Absolute, out var custom) && UpdateInstaller.IsTrusted(custom)
                ? custom
                : new Uri(GitHubUpdateFeed.DefaultUrl);
            services.AddHttpClient("updates", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(20);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Notka-Updater");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            });
            services.AddHttpClient("update-download", client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Notka-Updater");
            });
            services.AddSingleton<IUpdateFeed>(provider =>
                new GitHubUpdateFeed(provider.GetRequiredService<IHttpClientFactory>().CreateClient("updates"), feedUrl));
            services.AddSingleton<IUpdateInstaller>(provider =>
                new UpdateInstaller(provider.GetRequiredService<IHttpClientFactory>().CreateClient("update-download"), current));
            services.AddSingleton(provider => new UpdateViewModel(
                provider.GetRequiredService<IUpdateFeed>(),
                provider.GetRequiredService<IUpdateInstaller>(),
                provider.GetRequiredService<IAppLifetime>(),
                provider.GetRequiredService<AppSettings>(),
                current));
        }

        private static void ShowPreviousUpdateResult(UpdateViewModel updates)
        {
            try
            {
                if (!File.Exists(UpdateInstaller.ResultPath))
                {
                    return;
                }

                var result = UpdateResult.TryParse(File.ReadAllText(UpdateInstaller.ResultPath));
                File.Delete(UpdateInstaller.ResultPath);
                if (result is not null)
                {
                    updates.ShowResult(result);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warning(ex, "Could not read the previous update result");
            }
        }

        private void StartUpdateChecks(UpdateViewModel updates)
        {
            if (updates.IsDevelopmentBuild)
            {
                return;
            }

            async void Check(DispatcherQueueTimer sender, object args)
            {
                if (updates.AutoCheck)
                {
                    await updates.CheckAsync(manual: false);
                }
            }

            var first = _dispatcher!.CreateTimer();
            first.Interval = TimeSpan.FromSeconds(20);
            first.IsRepeating = false;
            first.Tick += Check;
            var daily = _dispatcher.CreateTimer();
            daily.Interval = TimeSpan.FromHours(24);
            daily.Tick += Check;
            _updateTimers = [first, daily];
            first.Start();
            daily.Start();
        }

        private void CreateTray()
        {
            var open = new MenuFlyoutItem { Text = "Otwórz Notkę", Command = new RelayCommand(ShowMainWindow) };
            var exit = new MenuFlyoutItem { Text = "Zakończ", Command = new AsyncRelayCommand(ExitAsync) };

            _tray = new TaskbarIcon
            {
                ToolTipText = "Notka",
                IconSource = new GeneratedIconSource
                {
                    Text = "N",
                    Foreground = new SolidColorBrush(Colors.White),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 110, 110)),
                },
                ContextFlyout = new MenuFlyout { Items = { open, exit } },
                LeftClickCommand = new RelayCommand(ShowMainWindow),
                NoLeftClickDelay = true,
            };
            _tray.ForceCreate();
        }

        private void ShowMainWindow()
        {
            if (_window is null)
            {
                return;
            }

            _window.AppWindow.Show();
            _window.Activate();
            ForegroundWindow.Bring(WinRT.Interop.WindowNative.GetWindowHandle(_window));
        }

        public Task<bool> TryExitAsync() => ExitAsync();

        private async Task<bool> ExitAsync()
        {
            if (_exiting || _window is null || _viewModel is null)
            {
                return _exiting;
            }

            _exiting = true;
            try
            {
                await _viewModel.Notes.SaveDirtyAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not save notes before exit");
            }

            var warnedRecently = _exitWarnedAt is { } warnedAt && DateTime.UtcNow - warnedAt < TimeSpan.FromSeconds(30);
            if (_viewModel.Notes.HasUnsavedChanges && !warnedRecently)
            {
                _exitWarnedAt = DateTime.UtcNow;
                _exiting = false;
                ShowMainWindow();
                return false;
            }

            _window.StoreCurrentSize();
            _window.AllowClose = true;
            _window.Close();
            await ShutdownAsync();
            return true;
        }

        private async Task ShutdownAsync()
        {
            if (_shutDown)
            {
                return;
            }

            _shutDown = true;
            foreach (var timer in _updateTimers)
            {
                timer.Stop();
            }

            try
            {
                _reminders?.Dispose();
                _strip?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error while disposing the taskbar strip");
            }

            try
            {
                if (_ui is not null)
                {
                    await _ui.DisposeAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error while disposing the UI services");
            }

            try
            {
                await _api.StopAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error while stopping the API host");
            }
            finally
            {
                _tray?.Dispose();
                _updateInstaller?.LaunchStaged();
                Exit();
            }
        }

        private static void ConfigureLogging()
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notka", "logs", "notka-.log");
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
                .WriteTo.File(path, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
                .CreateLogger();
        }

        private static void ShowStartupError(string message)
        {
            var window = new Window
            {
                Title = "Notka",
                SystemBackdrop = new MicaBackdrop(),
                Content = new TextBlock
                {
                    Text = message + @" Szczegóły są w logu: %LOCALAPPDATA%\Notka\logs.",
                    Margin = new Thickness(24),
                    TextWrapping = TextWrapping.Wrap,
                },
            };
            window.AppWindow.Resize(new SizeInt32(560, 240));
            window.Closed += (_, _) => Current.Exit();
            window.Activate();
        }
    }
}
