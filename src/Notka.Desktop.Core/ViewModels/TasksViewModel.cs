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

        private List<TaskItem> _loaded = [];

        public string EmptyText => !string.IsNullOrWhiteSpace(SearchText) ? "Brak pasujących zadań." : SelectedTab switch
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
        [NotifyPropertyChangedFor(nameof(EmptyText))]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private bool _isSearchOpen;

        [RelayCommand]
        private void ToggleSearch()
        {
            IsSearchOpen = !IsSearchOpen;
            if (!IsSearchOpen)
            {
                SearchText = string.Empty;
            }
        }

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

                _loaded = tasks;
                ApplyFilter();
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
                SearchText = string.Empty;
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

        partial void OnSearchTextChanged(string value) => ApplyFilter();

        private void ApplyFilter()
        {
            var query = SearchText.Trim();
            Items.Clear();
            foreach (var task in _loaded.Where(t => query.Length == 0 || t.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
            {
                Items.Add(new TaskRowViewModel(task, CompleteAsync));
            }
        }

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
