using CommunityToolkit.Mvvm.ComponentModel;
using Notka.Desktop.Services;

namespace Notka.Desktop.ViewModels
{
    public partial class MainViewModel(INotkaApiClient api, IChangesClient changes) : ObservableObject
    {
        private readonly IChangesClient _changes = changes;

        public NotesViewModel Notes { get; } = new(api);

        public TasksViewModel Tasks { get; } = new(api);

        public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(5);

        [ObservableProperty]
        private string _status = string.Empty;

        public async Task InitializeAsync()
        {
            _changes.TaskChanged += e => _ = Tasks.RefreshAsync();
            _changes.NoteChanged += e => _ = Notes.RefreshAsync();
            _changes.Reconnected += () => _ = Task.WhenAll(Tasks.RefreshAsync(), Notes.RefreshAsync());

            await Task.WhenAll(Notes.RefreshAsync(), Tasks.RefreshAsync());
            _ = ConnectAsync();
        }

        private async Task ConnectAsync()
        {
            while (true)
            {
                try
                {
                    await _changes.StartAsync();
                    Status = string.Empty;
                    return;
                }
                catch (Exception)
                {
                    Status = "Odświeżanie na żywo nie działa. Próbuję połączyć się ponownie.";
                    await Task.Delay(RetryDelay);
                }
            }
        }
    }
}
