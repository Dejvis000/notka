using Notka.Desktop.Services;
using Notka.Desktop.Taskbar;
using Notka.Desktop.ViewModels;

namespace Notka.Tests
{
    public sealed class SettingsViewModelTests : IDisposable
    {
        private readonly FakeAutostart _autostart = new();
        private readonly FakeLifetime _lifetime = new();

        private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), "notka-tests", $"settings-{Guid.NewGuid():N}.json");

        public void Dispose()
        {
            File.Delete(_settingsPath);
            File.Delete(_settingsPath + ".tmp");
        }

        private SettingsViewModel Create() => new(_autostart, _lifetime, AppSettings.Load(_settingsPath));

        [Fact]
        public void The_strip_monitor_choice_lists_connected_monitors_and_is_remembered()
        {
            var monitors = new FakeMonitors([new("\\\\.\\DISPLAY1", "Monitor 1"), new("\\\\.\\DISPLAY2", "Monitor 2")]);
            var settings = new SettingsViewModel(_autostart, _lifetime, AppSettings.Load(_settingsPath), monitors);
            var announced = new List<string>();
            settings.StripTargetChanged += announced.Add;

            Assert.Equal([StripTargets.Primary, StripTargets.All, "\\\\.\\DISPLAY1", "\\\\.\\DISPLAY2"], settings.StripTargetOptions.Select(o => o.Id));
            Assert.Equal(StripTargets.Primary, settings.StripTarget);

            settings.StripTarget = "\\\\.\\DISPLAY2";
            settings.StripTarget = null!;

            Assert.Equal(["\\\\.\\DISPLAY2"], announced);
            Assert.Equal("\\\\.\\DISPLAY2", Create().StripTarget);
        }

        [Fact]
        public void The_strip_background_is_on_by_default_and_the_choice_is_saved_and_announced()
        {
            var settings = Create();
            var announced = new List<bool>();
            settings.StripBackgroundChanged += announced.Add;
            Assert.True(settings.StripBackground);

            settings.StripBackground = false;

            Assert.Equal([false], announced);
            Assert.False(Create().StripBackground);
        }

        [Fact]
        public void A_remembered_monitor_that_is_no_longer_connected_stays_selectable_as_unavailable()
        {
            var settings = new SettingsViewModel(_autostart, _lifetime, AppSettings.Load(_settingsPath), new FakeMonitors([new("\\\\.\\DISPLAY2", "Monitor 2")]));
            settings.StripTarget = "\\\\.\\DISPLAY2";

            var reloaded = new SettingsViewModel(_autostart, _lifetime, AppSettings.Load(_settingsPath), new FakeMonitors([]));

            var option = Assert.Single(reloaded.StripTargetOptions, o => o.Id == "\\\\.\\DISPLAY2");
            Assert.Contains("Odłączony", option.Label);
            Assert.Equal("\\\\.\\DISPLAY2", reloaded.StripTarget);
        }

        [Fact]
        public void The_strip_switch_is_saved_reloaded_and_announced()
        {
            var settings = Create();
            var announced = new List<bool>();
            settings.ShowStripChanged += announced.Add;

            settings.ShowStrip = false;

            Assert.Equal([false], announced);
            Assert.False(Create().ShowStrip);
            Assert.Equal("Pasek jest wyłączony.", settings.StripStatus);
        }

        [Fact]
        public void The_strip_is_on_by_default_and_reports_its_state()
        {
            var settings = Create();
            Assert.True(settings.ShowStrip);

            settings.ReportStripState(StripState.Visible);
            Assert.Contains("widoczny", settings.StripStatus);

            settings.ReportStripState(StripState.Unavailable);
            Assert.Contains("niedostępny", settings.StripStatus);
        }

        [Fact]
        public void A_corrupt_settings_file_falls_back_to_defaults()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, "{ not json");

            Assert.True(Create().ShowStrip);
        }

        [Fact]
        public void The_gear_toggles_between_the_main_view_and_the_settings_view()
        {
            var settings = Create();
            Assert.False(settings.IsOpen);

            settings.ToggleCommand.Execute(null);
            Assert.True(settings.IsOpen);

            settings.ToggleCommand.Execute(null);
            Assert.False(settings.IsOpen);
        }

        [Fact]
        public void The_back_button_and_escape_close_the_settings_view()
        {
            var settings = Create();
            settings.ToggleCommand.Execute(null);

            settings.CloseCommand.Execute(null);

            Assert.False(settings.IsOpen);
        }

        [Fact]
        public void The_switch_starts_from_the_real_autostart_state()
        {
            _autostart.IsEnabled = true;

            Assert.True(Create().StartWithWindows);
        }

        [Fact]
        public void Turning_the_switch_on_and_off_changes_the_autostart_entry()
        {
            var settings = Create();

            settings.StartWithWindows = true;
            Assert.True(_autostart.IsEnabled);

            settings.StartWithWindows = false;
            Assert.False(_autostart.IsEnabled);
        }

        [Fact]
        public void A_failure_reverts_the_switch_and_shows_an_error()
        {
            _autostart.Fail = true;
            var settings = Create();

            settings.StartWithWindows = true;

            Assert.False(settings.StartWithWindows);
            Assert.Equal("Nie udało się zmienić uruchamiania z Windows.", settings.Error);
        }

        [Fact]
        public void The_exit_button_asks_the_app_to_exit()
        {
            Create().ExitCommand.Execute(null);

            Assert.Equal(1, _lifetime.ExitRequests);
        }

        private sealed class FakeMonitors(IReadOnlyList<StripTargetOption> monitors) : IMonitorLister
        {
            public IReadOnlyList<StripTargetOption> List() => monitors;
        }

        private sealed class FakeAutostart : IAutostart
        {
            public bool IsEnabled { get; set; }

            public bool Fail { get; set; }

            public void Enable() => Set(true);

            public void Disable() => Set(false);

            private void Set(bool value)
            {
                if (Fail)
                {
                    throw new UnauthorizedAccessException();
                }

                IsEnabled = value;
            }
        }

        private sealed class FakeLifetime : IAppLifetime
        {
            public int ExitRequests { get; private set; }

            public void RequestExit() => ExitRequests++;
        }
    }
}
