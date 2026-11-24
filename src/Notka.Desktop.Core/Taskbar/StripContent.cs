using Notka.Core;

namespace Notka.Desktop.Taskbar
{
    public sealed record StripContent(string Text, IReadOnlyList<string> CardLines)
    {
        public static StripContent Unavailable { get; } = new("Notka: brak danych", ["Brak danych z Notki."]);

        public static StripContent From(IReadOnlyList<TaskItem> today, int overdueCount, DateTime now)
        {
            var open = today.Where(t => !t.IsDone).ToList();
            var lines = open.Select(Describe).ToList();
            if (overdueCount > 0)
            {
                lines.Add($"Zaległe: {overdueCount}");
            }

            if (open.Count == 0)
            {
                return new StripContent(overdueCount > 0 ? $"! {overdueCount} zaległe" : "Brak zadań na dziś", lines);
            }

            var next = open
                .Where(t => HasTime(t) && t.DueAt >= now)
                .OrderBy(t => t.DueAt)
                .FirstOrDefault() ?? open[0];
            var text = $"{open.Count} · {Describe(next)}";
            if (overdueCount > 0)
            {
                text += $"  ! {overdueCount}";
            }

            return new StripContent(text, lines);
        }

        private static bool HasTime(TaskItem task) => task.DueAt is { } due && due.TimeOfDay != TimeSpan.Zero;

        private static string Describe(TaskItem task) => HasTime(task) ? $"{task.DueAt:HH:mm} {task.Title}" : task.Title;
    }
}
