using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Notka.Core;
using Notka.Desktop.Services;

namespace Notka.Desktop.ViewModels
{
    public enum TaskTab
    {
        Today,
        Overdue,
        Upcoming,
        NoDate,
        Done,
    }

    public partial class TasksViewModel(INotkaApiClient api) : ObservableObject
    {
        private readonly INotkaApiClient _api = api;
        private int _version;

        public ObservableCollection<TaskRowViewModel> Items { get; } = [];

        public string EmptyText => SelectedTab switch
        {
            TaskTab.Today => "Na dziś nic nie zostało.",
            TaskTab.Overdue => "Nic nie jest zaległe.",
            TaskTab.Upcoming => "Nic nie czeka na później.",
            TaskTab.NoDate => "Wszystkie zadania mają termin.",
            _ => "Nic jeszcze nie zrobiono.",
        };

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EmptyText))]
        private TaskTab _selectedTab = TaskTab.Today;

        [ObservableProperty]
        private string _newTitle = string.Empty;

        [ObservableProperty]
        private DateTime? _newDueDate;

        [ObservableProperty]
        private string? _error;

        [RelayCommand]
        public async Task RefreshAsync()
        {
            var version = ++_version;
            try
            {
                var tab = SelectedTab;
                var tasks = await _api.ListTasksAsync(tab switch
                {
                    TaskTab.Today => "Today",
                    TaskTab.Overdue => "Overdue",
                    TaskTab.Upcoming => "Upcoming",
                    TaskTab.Done => "Done",
                    _ => "All",
                });
                if (version != _version)
                {
                    return;
                }

                if (tab == TaskTab.NoDate)
                {
                    tasks = tasks.Where(t => t.DueAt is null && !t.IsDone).ToList();
                }

                Items.Clear();
                foreach (var task in tasks)
                {
                    Items.Add(new TaskRowViewModel(task, CompleteAsync));
                }

                Error = null;
            }
            catch (NotkaApiException ex)
            {
                Error = ex.Message;
            }
        }

        [RelayCommand]
        public async Task AddAsync()
        {
            var title = NewTitle.Trim();
            if (title.Length == 0)
            {
                return;
            }

            TaskItem created;
            try
            {
                var dueAt = NewDueDate ?? (SelectedTab == TaskTab.Today ? DateTime.Today : null);
                created = await _api.CreateTaskAsync(new TaskRequest { Title = title, DueAt = dueAt });
                NewTitle = string.Empty;
                NewDueDate = null;
                Error = null;
            }
            catch (NotkaApiException ex)
            {
                Error = ex.Message;
                return;
            }

            var target = TabFor(created);
            if (target != SelectedTab)
            {
                SelectedTab = target;
                return;
            }

            await RefreshAsync();
        }

        private static TaskTab TabFor(TaskItem task)
        {
            if (task.DueAt is not { } due)
            {
                return TaskTab.NoDate;
            }

            return due < DateTime.Today ? TaskTab.Overdue : due < DateTime.Today.AddDays(1) ? TaskTab.Today : TaskTab.Upcoming;
        }

        partial void OnSelectedTabChanged(TaskTab value) => _ = RefreshAsync();

        private async Task CompleteAsync(TaskRowViewModel row)
        {
            try
            {
                await _api.CompleteTaskAsync(row.Id);
            }
            catch (NotkaApiException ex)
            {
                Error = ex.Message;
                return;
            }

            await RefreshAsync();
        }
    }
}
