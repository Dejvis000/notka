using Notka.Desktop.Services;
using Notka.Desktop.ViewModels;

namespace Notka.Tests
{
    public sealed class WindowModeViewModelTests : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "notka-tests", $"mode-{Guid.NewGuid():N}.json");

        public void Dispose()
        {
            File.Delete(_path);
            File.Delete(_path + ".tmp");
        }

        private WindowModeViewModel Create() => new(AppSettings.Load(_path));

        [Fact]
        public void The_window_starts_in_the_full_view_without_staying_on_top()
        {
            var mode = Create();

            Assert.False(mode.IsCompact);
            Assert.False(mode.AlwaysOnTop);
            Assert.False(mode.KeepOnTop);
        }

        [Fact]
        public void The_compact_section_defaults_to_notes_and_the_tasks_choice_is_remembered()
        {
            var mode = Create();
            Assert.True(mode.ShowNotes);

            mode.ShowTasks = true;

            var reloaded = Create();
            Assert.True(reloaded.ShowTasks);
            Assert.False(reloaded.ShowNotes);
        }

        [Fact]
        public void The_compact_view_and_always_on_top_are_remembered()
        {
            var mode = Create();

            mode.ToggleCompactCommand.Execute(null);
            mode.ToggleAlwaysOnTopCommand.Execute(null);

            var reloaded = Create();
            Assert.True(reloaded.IsCompact);
            Assert.True(reloaded.AlwaysOnTop);
        }

        [Fact]
        public void Staying_on_top_only_applies_in_the_compact_view()
        {
            var mode = Create();
            mode.ToggleAlwaysOnTopCommand.Execute(null);
            Assert.False(mode.KeepOnTop);

            mode.ToggleCompactCommand.Execute(null);
            Assert.True(mode.KeepOnTop);

            mode.ToggleCompactCommand.Execute(null);
            Assert.False(mode.KeepOnTop);
        }

        [Fact]
        public void Each_view_remembers_its_own_size_and_falls_back_to_defaults()
        {
            var mode = Create();
            Assert.Equal((WindowModeViewModel.DefaultNormalWidth, WindowModeViewModel.DefaultNormalHeight), mode.GetSize(compact: false));
            Assert.Equal((WindowModeViewModel.DefaultCompactWidth, WindowModeViewModel.DefaultCompactHeight), mode.GetSize(compact: true));

            mode.SetSize(compact: true, 420, 700);
            mode.SetSize(compact: false, 1000, 650);

            var reloaded = Create();
            Assert.Equal((420, 700), reloaded.GetSize(compact: true));
            Assert.Equal((1000, 650), reloaded.GetSize(compact: false));
        }

        [Fact]
        public void A_tiny_size_is_not_stored()
        {
            var mode = Create();

            mode.SetSize(compact: true, 50, 50);

            Assert.Equal((WindowModeViewModel.DefaultCompactWidth, WindowModeViewModel.DefaultCompactHeight), mode.GetSize(compact: true));
        }
    }
}
