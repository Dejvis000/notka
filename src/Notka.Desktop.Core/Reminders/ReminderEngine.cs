using System.Globalization;
using System.Text.Json;
using Notka.Core;

namespace Notka.Desktop.Reminders
{
    public enum ReminderKind
    {
        Task,
        Summary,
    }

    public sealed record ReminderToast(ReminderKind Kind, int? TaskId, string Title, string Body);

    public sealed record Snooze(DateTime Until, long DueTicks);

    public sealed class ReminderState
    {
        public HashSet<string> Fired { get; } = [];

        public DateOnly? SummaryDate { get; set; }

        public Dictionary<int, Snooze> Snoozed { get; } = [];

        public static ReminderState FromJson(string json)
        {
            var state = new ReminderState();
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (root.TryGetProperty("fired", out var fired) && fired.ValueKind == JsonValueKind.Array)
                {
                    foreach (var key in fired.EnumerateArray().Select(e => e.GetString()).OfType<string>())
                    {
                        state.Fired.Add(key);
                    }
                }

                if (root.TryGetProperty("summaryDate", out var date)
                    && DateOnly.TryParseExact(date.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                {
                    state.SummaryDate = parsed;
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                return new ReminderState();
            }

            return state;
        }

        public string ToJson() => JsonSerializer.Serialize(new
        {
            fired = Fired.OrderBy(k => k, StringComparer.Ordinal),
            summaryDate = SummaryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        });
    }

    public static class ReminderEngine
    {
        public static readonly TimeSpan Lead = TimeSpan.FromMinutes(15);
        public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(60);
        public static readonly TimeSpan SnoozeLength = TimeSpan.FromMinutes(15);
        public static readonly TimeSpan SummaryFrom = TimeSpan.FromHours(9);
        public static readonly TimeSpan SummaryUntil = TimeSpan.FromHours(18);

        private static readonly TimeSpan KeepFiredFor = TimeSpan.FromDays(2);

        public static IReadOnlyList<ReminderToast> Evaluate(DateTime now, IEnumerable<TaskItem> tasks, ReminderState state)
        {
            var open = tasks.Where(t => !t.IsDone && t.DueAt is not null).ToList();
            var toasts = new List<ReminderToast>();
            Prune(now, state);

            foreach (var task in open.Where(t => HasTime(t.DueAt!.Value)).OrderBy(t => t.DueAt))
            {
                if (ShouldFire(now, task, state))
                {
                    toasts.Add(new ReminderToast(ReminderKind.Task, task.Id, task.Title, TaskBody(now, task.DueAt!.Value)));
                }
            }

            if (SummaryIsDue(now, state))
            {
                state.SummaryDate = DateOnly.FromDateTime(now);
                var today = now.Date;
                var dated = open.Count(t => t.DueAt!.Value.Date == today && !HasTime(t.DueAt.Value));
                var overdue = open.Count(t => t.DueAt!.Value.Date < today);
                if (dated + overdue > 0)
                {
                    toasts.Add(new ReminderToast(ReminderKind.Summary, null, "Dziś w Notce", SummaryBody(dated, overdue)));
                }
            }

            return toasts;
        }

        public static void Snooze(DateTime now, TaskItem task, ReminderState state)
        {
            if (task.DueAt is { } due)
            {
                state.Snoozed[task.Id] = new Snooze(now + SnoozeLength, due.Ticks);
            }
        }

        public static bool HasTime(DateTime due) => due.TimeOfDay != TimeSpan.Zero;

        public static string Plural(int count, string one, string few, string many)
        {
            var lastTwo = count % 100;
            var last = count % 10;
            return count == 1 ? one : last is >= 2 and <= 4 && lastTwo is < 12 or > 14 ? few : many;
        }

        private static bool ShouldFire(DateTime now, TaskItem task, ReminderState state)
        {
            var due = task.DueAt!.Value;
            if (state.Snoozed.TryGetValue(task.Id, out var snooze))
            {
                if (snooze.DueTicks != due.Ticks)
                {
                    state.Snoozed.Remove(task.Id);
                }
                else if (snooze.Until > now)
                {
                    return false;
                }
                else
                {
                    state.Snoozed.Remove(task.Id);
                    state.Fired.Add(Key(task));
                    return true;
                }
            }

            var key = Key(task);
            if (state.Fired.Contains(key) || now < due - Lead || now > due + StaleAfter)
            {
                return false;
            }

            state.Fired.Add(key);
            return true;
        }

        private static bool SummaryIsDue(DateTime now, ReminderState state) =>
            state.SummaryDate != DateOnly.FromDateTime(now) && now.TimeOfDay >= SummaryFrom && now.TimeOfDay < SummaryUntil;

        private static string Key(TaskItem task) =>
            string.Create(CultureInfo.InvariantCulture, $"{task.Id}|{task.DueAt!.Value.Ticks}");

        private static void Prune(DateTime now, ReminderState state)
        {
            state.Fired.RemoveWhere(key =>
                long.TryParse(key.AsSpan(key.IndexOf('|') + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && new DateTime(Math.Clamp(ticks, DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks)) < now - KeepFiredFor);
        }

        private static string TaskBody(DateTime now, DateTime due)
        {
            var minutes = (int)Math.Ceiling((due - now).TotalMinutes);
            return minutes > 0
                ? $"Za {minutes} min, o {due:HH:mm}"
                : $"Termin minął o {due:HH:mm}";
        }

        private static string SummaryBody(int dated, int overdue)
        {
            var parts = new List<string>();
            if (dated > 0)
            {
                parts.Add($"{dated} {Plural(dated, "zadanie", "zadania", "zadań")} na dziś");
            }

            if (overdue > 0)
            {
                parts.Add($"{overdue} {Plural(overdue, "zaległe", "zaległe", "zaległych")}");
            }

            return string.Join(", ", parts);
        }
    }
}
