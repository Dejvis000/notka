using Notka.Core;
using Notka.Desktop.Taskbar;

namespace Notka.Tests
{
    public sealed class TaskbarStripTests
    {
        private static readonly DateTime Now = new(2026, 11, 24, 12, 0, 0);

        private static TaskItem Task(string title, int? hour = null, bool done = false) =>
            new() { Title = title, DueAt = hour is { } h ? Now.Date.AddHours(h) : Now.Date, IsDone = done };

        [Fact]
        public void With_nothing_to_do_the_strip_says_so()
        {
            var content = StripContent.From([], 0, Now);

            Assert.Equal("Brak zadań na dziś", content.Text);
            Assert.Empty(content.CardLines);
        }

        [Fact]
        public void The_strip_shows_the_count_and_the_next_task_with_its_time()
        {
            var content = StripContent.From([Task("Raport", 9), Task("Panel Wycen", 14), Task("Zakupy", 18)], 0, Now);

            Assert.Equal("3 · 14:00 Panel Wycen", content.Text);
            Assert.Equal(["09:00 Raport", "14:00 Panel Wycen", "18:00 Zakupy"], content.CardLines);
        }

        [Fact]
        public void When_every_timed_task_is_past_the_first_task_is_shown()
        {
            var content = StripContent.From([Task("Raport", 9), Task("Bez godziny")], 0, Now);

            Assert.Equal("2 · 09:00 Raport", content.Text);
        }

        [Fact]
        public void A_task_without_a_time_is_shown_by_its_title_only()
        {
            var content = StripContent.From([Task("Bez godziny")], 0, Now);

            Assert.Equal("1 · Bez godziny", content.Text);
        }

        [Fact]
        public void Done_tasks_are_not_counted()
        {
            var content = StripContent.From([Task("A", 14), Task("B", 15, done: true)], 0, Now);

            Assert.Equal("1 · 14:00 A", content.Text);
        }

        [Fact]
        public void Overdue_tasks_are_flagged_next_to_the_summary()
        {
            Assert.Equal("1 · 14:00 A  ! 2", StripContent.From([Task("A", 14)], 2, Now).Text);
            Assert.Equal("! 2 zaległe", StripContent.From([], 2, Now).Text);
            Assert.Contains("Zaległe: 2", StripContent.From([Task("A", 14)], 2, Now).CardLines);
        }

        private static readonly PixelRect Taskbar = new(0, 1204, 2278, 1252);
        private static readonly PixelRect Tray = new(2000, 1204, 2278, 1252);
        private static readonly PixelRect Widgets = new(1836, 1204, 1988, 1252);
        private static readonly PixelRect Buttons = new(1100, 1204, 1250, 1252);

        [Fact]
        public void The_strip_sits_left_of_the_notification_area_and_is_centered_vertically()
        {
            var rect = StripPlacement.Compute(Taskbar, Tray, null, Buttons, 240, 36, 8, 120);

            Assert.Equal(new PixelRect(1752, 1210, 1992, 1246), rect);
        }

        [Fact]
        public void The_strip_avoids_the_widgets_button()
        {
            var rect = StripPlacement.Compute(Taskbar, Tray, Widgets, Buttons, 240, 36, 8, 120);

            Assert.Equal(1828, rect!.Value.Right);
            Assert.Equal(240, rect.Value.Width);
        }

        [Fact]
        public void A_widgets_button_at_the_left_edge_of_a_centered_taskbar_does_not_block_the_strip()
        {
            var centeredButtons = new PixelRect(900, 1204, 1380, 1252);
            var widgetsAtLeft = new PixelRect(0, 1204, 152, 1252);

            var rect = StripPlacement.Compute(Taskbar, Tray, widgetsAtLeft, centeredButtons, 240, 36, 8, 120);

            Assert.Equal(1992, rect!.Value.Right);
        }

        [Fact]
        public void A_zero_height_strip_is_never_placed()
        {
            Assert.Null(StripPlacement.Compute(Taskbar, Tray, null, Buttons, 240, 0, 8, 120));
        }

        [Fact]
        public void A_widgets_button_right_of_the_tray_is_ignored()
        {
            var rect = StripPlacement.Compute(Taskbar, Tray, new PixelRect(2100, 1204, 2200, 1252), Buttons, 240, 36, 8, 120);

            Assert.Equal(1992, rect!.Value.Right);
        }

        [Fact]
        public void The_strip_shrinks_to_the_free_space_and_hides_when_it_is_too_narrow()
        {
            var shrunk = StripPlacement.Compute(Taskbar, Tray, null, new PixelRect(0, 1204, 1800, 1252), 240, 36, 8, 120);
            var hidden = StripPlacement.Compute(Taskbar, Tray, null, new PixelRect(0, 1204, 1900, 1252), 240, 36, 8, 120);

            Assert.Equal(184, shrunk!.Value.Width);
            Assert.Equal(1992, shrunk.Value.Right);
            Assert.Null(hidden);
        }

        [Fact]
        public void An_unknown_task_button_position_hides_the_strip()
        {
            Assert.Null(StripPlacement.Compute(Taskbar, Tray, null, null, 240, 36, 8, 120));
        }

        [Fact]
        public void A_vertical_or_collapsed_taskbar_hides_the_strip()
        {
            Assert.Null(StripPlacement.Compute(new PixelRect(0, 0, 48, 1200), Tray, null, Buttons, 240, 36, 8, 120));
            Assert.Null(StripPlacement.Compute(new PixelRect(0, 1250, 2278, 1252), Tray, null, Buttons, 240, 36, 8, 120));
        }

        [Fact]
        public void The_strip_is_never_taller_than_the_taskbar()
        {
            var rect = StripPlacement.Compute(new PixelRect(0, 1204, 2278, 1228), Tray, null, Buttons, 240, 36, 8, 120);

            Assert.Equal(24, rect!.Value.Height);
        }

        [Fact]
        public void The_tracker_falls_back_after_three_failures_and_recovers_on_success()
        {
            var tracker = new PlacementTracker();
            Assert.Equal(StripState.Starting, tracker.State);

            Assert.False(tracker.Report(false));
            Assert.False(tracker.Report(false));
            Assert.True(tracker.Report(false));
            Assert.Equal(StripState.Unavailable, tracker.State);

            Assert.True(tracker.Report(true));
            Assert.Equal(StripState.Visible, tracker.State);
        }

        [Fact]
        public void Failed_ticks_must_be_consecutive_to_count_as_unavailable()
        {
            var tracker = new PlacementTracker();
            tracker.Report(true);

            tracker.Report(false);
            tracker.Report(false);
            tracker.Report(true);
            tracker.Report(false);
            tracker.Report(false);

            Assert.Equal(StripState.Visible, tracker.State);
        }
    }
}
