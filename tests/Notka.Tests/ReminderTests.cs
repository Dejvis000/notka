using Notka.Core;
using Notka.Desktop.Reminders;

namespace Notka.Tests
{
    public sealed class ReminderTests
    {
        private static readonly DateTime Noon = new(2026, 11, 25, 12, 0, 0);

        private static TaskItem Task(int id, string title, DateTime? due, bool done = false) =>
            new() { Id = id, Title = title, DueAt = due, IsDone = done };

        private static IReadOnlyList<ReminderToast> Evaluate(DateTime now, ReminderState state, params TaskItem[] tasks) =>
            ReminderEngine.Evaluate(now, tasks, state);

        [Fact]
        public void A_timed_task_is_announced_once_fifteen_minutes_before_its_time()
        {
            var state = new ReminderState();
            var task = Task(1, "Panel Wycen", Noon.AddHours(2));

            Assert.Empty(Evaluate(Noon.AddHours(1), state, task));

            var toast = Assert.Single(Evaluate(Noon.AddHours(1).AddMinutes(45), state, task));
            Assert.Equal(ReminderKind.Task, toast.Kind);
            Assert.Equal(1, toast.TaskId);
            Assert.Equal("Panel Wycen", toast.Title);
            Assert.Equal("Za 15 min, o 14:00", toast.Body);
            Assert.Empty(Evaluate(Noon.AddHours(1).AddMinutes(50), state, task));
        }

        [Fact]
        public void A_task_created_inside_the_lead_time_fires_immediately()
        {
            var toast = Assert.Single(Evaluate(Noon, new ReminderState(), Task(1, "Szybkie", Noon.AddMinutes(5))));

            Assert.Equal("Za 5 min, o 12:05", toast.Body);
        }

        [Fact]
        public void A_task_long_overdue_at_startup_is_not_announced_but_a_recent_one_is()
        {
            Assert.Empty(Evaluate(Noon, new ReminderState(), Task(1, "Stare", Noon.AddMinutes(-61))));

            var toast = Assert.Single(Evaluate(Noon, new ReminderState(), Task(2, "Świeże", Noon.AddMinutes(-10))));
            Assert.Equal("Termin minął o 11:50", toast.Body);
        }

        [Fact]
        public void Done_tasks_and_date_only_tasks_get_no_task_toast()
        {
            var state = new ReminderState();

            Assert.Empty(Evaluate(Noon, state, Task(1, "Zrobione", Noon.AddMinutes(5), done: true)));
            Assert.Empty(Evaluate(Noon, state, Task(2, "Tylko data", Noon.Date)));
            Assert.Empty(Evaluate(Noon, state, Task(3, "Bez terminu", null)));
        }

        [Fact]
        public void Moving_a_task_to_a_new_time_arms_its_reminder_again()
        {
            var state = new ReminderState();
            Assert.Single(Evaluate(Noon, state, Task(1, "Spotkanie", Noon.AddMinutes(10))));

            var moved = Task(1, "Spotkanie", Noon.AddHours(1));

            Assert.Empty(Evaluate(Noon.AddMinutes(2), state, moved));
            var toast = Assert.Single(Evaluate(Noon.AddMinutes(45), state, moved));
            Assert.Equal("Za 15 min, o 13:00", toast.Body);
        }

        [Fact]
        public void A_snoozed_task_waits_and_then_fires_again_even_past_the_stale_limit()
        {
            var state = new ReminderState();
            var task = Task(1, "Zadzwonić", Noon.AddMinutes(10));
            Assert.Single(Evaluate(Noon, state, task));

            ReminderEngine.Snooze(Noon.AddHours(2), task, state);

            Assert.Empty(Evaluate(Noon.AddHours(2).AddMinutes(10), state, task));
            var toast = Assert.Single(Evaluate(Noon.AddHours(2).AddMinutes(15), state, task));
            Assert.Equal("Zadzwonić", toast.Title);
            Assert.Empty(Evaluate(Noon.AddHours(2).AddMinutes(16), state, task));
        }

        [Fact]
        public void A_snoozed_task_that_is_rescheduled_keeps_its_new_reminder_for_the_new_time()
        {
            var state = new ReminderState();
            var original = Task(1, "Zadzwonić", Noon.AddMinutes(10));
            Assert.Single(Evaluate(Noon, state, original));
            ReminderEngine.Snooze(Noon.AddMinutes(5), original, state);

            var tomorrow = Task(1, "Zadzwonić", Noon.AddDays(1));
            Assert.Empty(Evaluate(Noon.AddMinutes(25), state, tomorrow));
            Assert.Empty(state.Snoozed);

            var toast = Assert.Single(Evaluate(Noon.AddDays(1).AddMinutes(-15), state, tomorrow));
            Assert.Equal("Za 15 min, o 12:00", toast.Body);
        }

        [Fact]
        public void The_morning_summary_counts_date_only_and_overdue_tasks_and_fires_once_a_day()
        {
            var state = new ReminderState();
            var tasks = new[]
            {
                Task(1, "A", Noon.Date),
                Task(2, "B", Noon.Date),
                Task(3, "C", Noon.Date),
                Task(4, "Zaległe", Noon.Date.AddDays(-2)),
                Task(5, "Z godziną", Noon.Date.AddHours(20)),
            };

            Assert.Empty(ReminderEngine.Evaluate(Noon.Date.AddHours(8), tasks, state));

            var toast = Assert.Single(ReminderEngine.Evaluate(Noon.Date.AddHours(9), tasks, state));
            Assert.Equal(ReminderKind.Summary, toast.Kind);
            Assert.Equal("3 zadania na dziś, 1 zaległe", toast.Body);
            Assert.Empty(ReminderEngine.Evaluate(Noon.Date.AddHours(10), tasks, state));
        }

        [Fact]
        public void The_summary_also_fires_when_the_app_starts_late_in_the_morning_but_not_in_the_evening()
        {
            var tasks = new[] { Task(1, "A", Noon.Date) };

            Assert.Single(ReminderEngine.Evaluate(Noon.Date.AddHours(11), tasks, new ReminderState()));
            Assert.Empty(ReminderEngine.Evaluate(Noon.Date.AddHours(18), tasks, new ReminderState()));
        }

        [Fact]
        public void An_empty_summary_is_marked_as_handled_and_not_retried()
        {
            var state = new ReminderState();

            Assert.Empty(ReminderEngine.Evaluate(Noon.Date.AddHours(9), [], state));
            Assert.Equal(DateOnly.FromDateTime(Noon), state.SummaryDate);

            Assert.Empty(ReminderEngine.Evaluate(Noon.Date.AddHours(10), [Task(1, "Dodane później", Noon.Date)], state));
        }

        [Theory]
        [InlineData(1, "zadanie")]
        [InlineData(2, "zadania")]
        [InlineData(4, "zadania")]
        [InlineData(5, "zadań")]
        [InlineData(12, "zadań")]
        [InlineData(22, "zadania")]
        [InlineData(25, "zadań")]
        public void Polish_plurals_follow_the_counting_rules(int count, string expected) =>
            Assert.Equal(expected, ReminderEngine.Plural(count, "zadanie", "zadania", "zadań"));

        [Fact]
        public void The_fired_keys_and_summary_date_survive_a_restart_but_snoozes_do_not()
        {
            var state = new ReminderState();
            Assert.Single(Evaluate(Noon, state, Task(1, "Spotkanie", Noon.AddMinutes(10))));
            ReminderEngine.Evaluate(Noon.Date.AddHours(9), [], state);
            ReminderEngine.Snooze(Noon, Task(7, "Odłożone", Noon.AddMinutes(5)), state);

            var restored = ReminderState.FromJson(state.ToJson());

            Assert.Empty(Evaluate(Noon.AddMinutes(1), restored, Task(1, "Spotkanie", Noon.AddMinutes(10))));
            Assert.Equal(state.SummaryDate, restored.SummaryDate);
            Assert.Empty(restored.Snoozed);
        }

        [Fact]
        public void A_corrupt_state_file_starts_fresh_and_old_keys_are_pruned()
        {
            Assert.Empty(ReminderState.FromJson("not json").Fired);

            var state = new ReminderState();
            state.Fired.Add($"1|{Noon.AddDays(-5).Ticks}");
            state.Fired.Add($"2|{Noon.AddHours(3).Ticks}");

            ReminderEngine.Evaluate(Noon, [], state);

            Assert.Equal([$"2|{Noon.AddHours(3).Ticks}"], state.Fired);
        }
    }
}
