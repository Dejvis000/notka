using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Notka.Core;
using Notka.Desktop.Services;

namespace Notka.Desktop.ViewModels
{
    public partial class NotesViewModel(INotkaApiClient api) : ObservableObject
    {
        private readonly INotkaApiClient _api = api;
        private readonly List<NoteViewModel> _all = [];
        private int _version;

        public ObservableCollection<NoteViewModel> Visible { get; } = [];

        public string EmptyText => string.IsNullOrWhiteSpace(SearchText)
            ? "Brak notatek. Wpisz coś powyżej i naciśnij Enter."
            : "Żadna notatka nie pasuje do wyszukiwania.";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EmptyText))]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private string _newNoteText = string.Empty;

        [ObservableProperty]
        private string? _error;

        [RelayCommand]
        public async Task RefreshAsync()
        {
            var version = ++_version;
            try
            {
                var notes = await _api.ListNotesAsync();
                if (version != _version)
                {
                    return;
                }

                var existing = _all.ToDictionary(n => n.Id);
                _all.Clear();
                foreach (var note in notes)
                {
                    if (existing.TryGetValue(note.Id, out var current))
                    {
                        current.Update(note);
                        _all.Add(current);
                    }
                    else
                    {
                        _all.Add(new NoteViewModel(note, this));
                    }
                }

                Error = null;
                ApplyFilter();
            }
            catch (NotkaApiException ex)
            {
                Error = ex.Message;
            }
        }

        [RelayCommand]
        public async Task AddAsync()
        {
            if (string.IsNullOrWhiteSpace(NewNoteText))
            {
                return;
            }

            if (await RunAsync(() => _api.CreateNoteAsync(new NoteRequest { Content = NewNoteText.Trim() })))
            {
                NewNoteText = string.Empty;
                await RefreshAsync();
            }
        }

        public bool HasUnsavedChanges => _all.Any(n => n.IsDirty);

        public async Task SaveDirtyAsync()
        {
            foreach (var note in _all.Where(n => n.IsDirty).ToList())
            {
                await SaveAsync(note);
            }
        }

        internal async Task SaveAsync(NoteViewModel note)
        {
            if (await RunAsync(() => _api.UpdateNoteAsync(note.Id, new NoteRequest { Content = note.Content, Color = note.Color })))
            {
                note.IsDirty = false;
                await RefreshAsync();
            }
        }

        internal async Task SetPinnedAsync(NoteViewModel note, bool pinned)
        {
            if (await RunAsync(() => _api.SetNotePinnedAsync(note.Id, pinned)))
            {
                await RefreshAsync();
            }
        }

        internal async Task SetColorAsync(NoteViewModel note, string color)
        {
            if (await RunAsync(() => _api.UpdateNoteAsync(note.Id, new NoteRequest { Content = note.Content, Color = color })))
            {
                note.IsDirty = false;
                await RefreshAsync();
            }
        }

        internal async Task DeleteAsync(NoteViewModel note)
        {
            if (await RunAsync(() => _api.DeleteNoteAsync(note.Id)))
            {
                await RefreshAsync();
            }
        }

        partial void OnSearchTextChanged(string value) => ApplyFilter();

        private async Task<bool> RunAsync(Func<Task> action)
        {
            try
            {
                await action();
                Error = null;
                return true;
            }
            catch (NotkaApiException ex)
            {
                Error = ex.Message;
                return false;
            }
        }

        private void ApplyFilter()
        {
            var query = SearchText.Trim();
            var desired = _all.Where(n => query.Length == 0 || n.Content.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

            for (var i = 0; i < desired.Count; i++)
            {
                if (i < Visible.Count && ReferenceEquals(Visible[i], desired[i]))
                {
                    continue;
                }

                var current = Visible.IndexOf(desired[i]);
                if (current > i)
                {
                    Visible.Move(current, i);
                }
                else
                {
                    Visible.Insert(i, desired[i]);
                }
            }

            while (Visible.Count > desired.Count)
            {
                Visible.RemoveAt(Visible.Count - 1);
            }
        }
    }
}
