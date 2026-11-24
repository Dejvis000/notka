using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Notka.Core;

namespace Notka.Desktop.ViewModels
{
    public partial class TaskRowViewModel(TaskItem task, Func<TaskRowViewModel, Task> complete) : ObservableObject
    {
        private static readonly CultureInfo Polish = new("pl-PL");

        private readonly TaskItem _task = task;
        private readonly Func<TaskRowViewModel, Task> _complete = complete;

        public int Id => _task.Id;

        public string Title => _task.Title;

        public bool IsDone => _task.IsDone;

        public bool IsOverdue => !_task.IsDone && _task.DueAt < DateTime.Today;

        public string DueText => FormatDue(_task.DueAt);

        public string PriorityText => _task.Priority == Priority.High ? "wysoki priorytet" : string.Empty;

        public override string ToString() => Title;

        [RelayCommand]
        private Task CompleteAsync() => _complete(this);

        private static string FormatDue(DateTime? dueAt)
        {
            if (dueAt is not { } due)
            {
                return string.Empty;
            }

            var time = due.TimeOfDay == TimeSpan.Zero ? string.Empty : $" {due.ToString("HH:mm", Polish)}";
            var day = due.Date == DateTime.Today ? "dziś"
                : due.Date == DateTime.Today.AddDays(1) ? "jutro"
                : due.ToString("d MMM", Polish);
            return day + time;
        }
    }
}
