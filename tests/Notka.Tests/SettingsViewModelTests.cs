using Notka.Desktop.Services;
using Notka.Desktop.ViewModels;

namespace Notka.Tests
{
    public sealed class SettingsViewModelTests
    {
        private readonly FakeAutostart _autostart = new();
        private readonly FakeLifetime _lifetime = new();

        private SettingsViewModel Create() => new(_autostart, _lifetime);

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
