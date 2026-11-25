using Notka.Desktop.Services;
using Notka.Desktop.ViewModels;
using Serilog;

namespace Notka.Desktop
{
    internal sealed class DesktopNotesManager : IDisposable
    {
        private readonly NotesViewModel _notes;
        private readonly DesktopNotesStore _store;
        private readonly Dictionary<int, DesktopNoteWindow> _windows = [];
        private bool _restored;
        private bool _shuttingDown;

        public DesktopNotesManager(NotesViewModel notes, DesktopNotesStore store)
        {
            _notes = notes;
            _store = store;
            _notes.Refreshed += OnRefreshed;
            _notes.DesktopRequested += OnDesktopRequested;
        }

        public void Dispose()
        {
            _notes.Refreshed -= OnRefreshed;
            _notes.DesktopRequested -= OnDesktopRequested;
        }

        public void Shutdown()
        {
            _shuttingDown = true;
            foreach (var window in _windows.Values.ToList())
            {
                window.Close();
            }

            _windows.Clear();
        }

        private void OnDesktopRequested(NoteViewModel note)
        {
            if (!_store.Contains(note.Id))
            {
                _store.Set(note.Id, new DesktopNoteState(0, 0, 0, 0, OnTop: false));
            }

            Open(note);
        }

        private void OnRefreshed(IReadOnlyList<NoteViewModel> notes)
        {
            foreach (var id in _store.Prune(notes.Select(n => n.Id)))
            {
                CloseWindow(id);
            }

            if (_restored)
            {
                return;
            }

            _restored = true;
            foreach (var id in _store.Ids.ToList())
            {
                if (notes.FirstOrDefault(n => n.Id == id) is { } note)
                {
                    Open(note);
                }
            }
        }

        private void Open(NoteViewModel note)
        {
            if (_shuttingDown)
            {
                return;
            }

            if (_windows.TryGetValue(note.Id, out var existing))
            {
                existing.Bring();
                return;
            }

            try
            {
                var id = note.Id;
                var window = new DesktopNoteWindow(note, _store.Get(id), _windows.Count, state =>
                {
                    if (_store.Contains(id))
                    {
                        _store.Set(id, state);
                    }
                });
                window.Closed += (_, _) =>
                {
                    if (_shuttingDown)
                    {
                        return;
                    }

                    _windows.Remove(id);
                    _store.Remove(id);
                };
                _windows[id] = window;
                window.Bring();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Could not open the desktop window for note {Id}", note.Id);
            }
        }

        private void CloseWindow(int id)
        {
            if (_windows.Remove(id, out var window))
            {
                window.Note.IsDirty = false;
                window.Close();
            }
        }
    }
}
