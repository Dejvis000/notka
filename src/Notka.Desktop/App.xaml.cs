using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Notka.Desktop.Services;
using Notka.Desktop.ViewModels;
using Serilog;
using Windows.Graphics;

namespace Notka.Desktop
{
    public partial class App : Application
    {
        private readonly ApiHost _api = new();
        private DispatcherQueue? _dispatcher;
        private ServiceProvider? _ui;
        private MainViewModel? _viewModel;
        private MainWindow? _window;
        private TaskbarIcon? _tray;
        private bool _exiting;
        private DateTime? _exitWarnedAt;
        private bool _shutDown;

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
                await _api.StartAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not start the API host");
                ShowStartupError("Nie udało się uruchomić Notki. Port jest prawdopodobnie zajęty przez inną aplikację. " +
                    "Ustaw inny port w zmiennej środowiskowej Notka__Port i uruchom aplikację ponownie.");
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
            var ui = BuildUiServices(_api.Url, _api.Token);
            _ui = ui;
            _viewModel = ui.GetRequiredService<MainViewModel>();
            _window = new MainWindow(_viewModel, new AutostartService(exePath), () => _ = ExitAsync());
            _window.Closed += async (_, _) => await ShutdownAsync();

            CreateTray();
            if (!Environment.GetCommandLineArgs().Contains(AutostartService.TrayArgument))
            {
                _window.Activate();
            }

            Program.ActivationHandler = () => _dispatcher?.TryEnqueue(ShowMainWindow);
        }

        private static ServiceProvider BuildUiServices(Uri apiUrl, string token)
        {
            var services = new ServiceCollection();
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
        }

        private async Task ExitAsync()
        {
            if (_exiting || _window is null || _viewModel is null)
            {
                return;
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
                return;
            }

            _window.AllowClose = true;
            _window.Close();
            await ShutdownAsync();
        }

        private async Task ShutdownAsync()
        {
            if (_shutDown)
            {
                return;
            }

            _shutDown = true;
            try
            {
                if (_ui is not null)
                {
                    await _ui.DisposeAsync();
                }

                await _api.StopAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error while shutting down");
            }
            finally
            {
                _tray?.Dispose();
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
