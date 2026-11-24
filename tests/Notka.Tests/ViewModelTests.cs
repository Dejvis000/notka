using Notka.Core;
using Notka.Desktop.Services;
using Notka.Desktop.ViewModels;

namespace Notka.Tests
{
    public sealed class ViewModelTests
    {
        private readonly FakeApi _api = new();

        private static TaskItem Task(string title, DateTime? dueAt = null, bool done = false) =>
            new() { Title = title, DueAt = dueAt, IsDone = done };

        [Fact]
        public async Task The_task_list_follows_the_selected_tab()
        {
            _api.AddTask(Task("wczoraj", DateTime.Today.AddDays(-1)));
            _api.AddTask(Task("dzis", DateTime.Today.AddHours(9)));
            _api.AddTask(Task("jutro", DateTime.Today.AddDays(1)));
            _api.AddTask(Task("bez terminu"));
            _api.AddTask(Task("zrobione", DateTime.Today, done: true));
            var tasks = new TasksViewModel(_api);

            await tasks.RefreshAsync();
            Assert.Equal(["dzis"], tasks.Items.Select(t => t.Title));

            foreach (var (tab, expected) in new[]
            {
                (TaskTab.Overdue, "wczoraj"),
                (TaskTab.Upcoming, "jutro"),
                (TaskTab.NoDate, "bez terminu"),
                (TaskTab.Done, "zrobione"),
            })
            {
                tasks.SelectedTab = tab;
                await tasks.RefreshAsync();
                Assert.Equal([expected], tasks.Items.Select(t => t.Title));
            }
        }

        [Fact]
        public async Task A_quick_added_task_on_the_Today_tab_is_due_today_and_the_title_is_cleared()
        {
            var tasks = new TasksViewModel(_api) { NewTitle = "  Oddać poprawki  " };

            await tasks.AddAsync();

            Assert.Equal("", tasks.NewTitle);
            var created = Assert.Single(_api.Tasks);
            Assert.Equal("Oddać poprawki", created.Title);
            Assert.Equal(DateTime.Today, created.DueAt);
            Assert.Equal(["Oddać poprawki"], tasks.Items.Select(t => t.Title));
        }

        [Fact]
        public async Task A_blank_title_does_not_create_a_task()
        {
            var tasks = new TasksViewModel(_api) { NewTitle = "   " };

            await tasks.AddAsync();

            Assert.Empty(_api.Tasks);
        }

        [Fact]
        public async Task Completing_a_task_removes_it_from_the_open_list()
        {
            _api.AddTask(Task("a", DateTime.Today));
            var tasks = new TasksViewModel(_api);
            await tasks.RefreshAsync();

            await tasks.Items[0].CompleteCommand.ExecuteAsync(null);

            Assert.Empty(tasks.Items);
            Assert.True(_api.Tasks[0].IsDone);
        }

        [Fact]
        public async Task An_API_failure_is_shown_as_an_error_instead_of_throwing()
        {
            _api.Fail = new NotkaApiException("Nie można połączyć się z API Notki.");
            var tasks = new TasksViewModel(_api);

            await tasks.RefreshAsync();

            Assert.Equal("Nie można połączyć się z API Notki.", tasks.Error);
        }

        [Fact]
        public async Task Due_dates_are_shown_in_a_short_Polish_form()
        {
            _api.AddTask(Task("a", DateTime.Today.AddHours(10)));
            _api.AddTask(Task("b", DateTime.Today.AddDays(1)));
            var tasks = new TasksViewModel(_api) { SelectedTab = TaskTab.Today };
            await tasks.RefreshAsync();
            Assert.Equal("dziś 10:00", tasks.Items[0].DueText);

            tasks.SelectedTab = TaskTab.Upcoming;
            await tasks.RefreshAsync();
            Assert.Equal("jutro", tasks.Items[0].DueText);
        }

        [Fact]
        public async Task Notes_are_filtered_by_the_search_text_ignoring_case_including_Polish_letters()
        {
            _api.AddNote(new StickyNote { Content = "Pojechać do Łodzi" });
            _api.AddNote(new StickyNote { Content = "Kupić kawę" });
            var notes = new NotesViewModel(_api);
            await notes.RefreshAsync();

            notes.SearchText = "łodzi";

            Assert.Equal(["Pojechać do Łodzi"], notes.Visible.Select(n => n.Content));
            notes.SearchText = "";
            Assert.Equal(2, notes.Visible.Count);
        }

        [Fact]
        public async Task A_note_can_be_added_pinned_recolored_and_deleted()
        {
            var notes = new NotesViewModel(_api) { NewNoteText = " zakupy " };

            await notes.AddAsync();
            var note = Assert.Single(notes.Visible);
            Assert.Equal("zakupy", note.Content);
            Assert.Equal("", notes.NewNoteText);

            await note.TogglePinCommand.ExecuteAsync(null);
            Assert.True(Assert.Single(notes.Visible).Pinned);

            await note.SetColorCommand.ExecuteAsync("blue");
            Assert.Equal("blue", Assert.Single(notes.Visible).Color);

            await note.DeleteCommand.ExecuteAsync(null);
            Assert.Empty(notes.Visible);
        }

        [Fact]
        public async Task An_edited_note_is_saved_and_unsaved_edits_survive_a_refresh()
        {
            _api.AddNote(new StickyNote { Content = "stara" });
            var notes = new NotesViewModel(_api);
            await notes.RefreshAsync();
            var note = notes.Visible[0];

            note.Content = "w trakcie pisania";
            await notes.RefreshAsync();
            Assert.Equal("w trakcie pisania", note.Content);
            Assert.True(note.IsDirty);

            await note.SaveCommand.ExecuteAsync(null);
            Assert.Equal("w trakcie pisania", _api.Notes[0].Content);
            Assert.False(note.IsDirty);
        }

        [Fact]
        public async Task Live_changes_and_reconnects_refresh_the_lists()
        {
            var changes = new FakeChanges();
            var main = new MainViewModel(_api, changes);
            await main.InitializeAsync();
            Assert.Empty(main.Tasks.Items);

            _api.AddTask(Task("nowy", DateTime.Today));
            changes.RaiseTaskChanged();
            await WaitUntil(() => main.Tasks.Items.Count == 1);

            _api.AddNote(new StickyNote { Content = "nowa" });
            changes.RaiseNoteChanged();
            await WaitUntil(() => main.Notes.Visible.Count == 1);

            _api.AddTask(Task("po reconnect", DateTime.Today));
            changes.RaiseReconnected();
            await WaitUntil(() => main.Tasks.Items.Count == 2);
        }

        [Fact]
        public async Task A_failing_live_connection_is_reported_but_the_data_still_loads()
        {
            _api.AddNote(new StickyNote { Content = "n" });
            var main = new MainViewModel(_api, new FakeChanges { StartFailures = int.MaxValue }) { RetryDelay = TimeSpan.FromMinutes(1) };

            await main.InitializeAsync();

            Assert.Contains("Odświeżanie na żywo nie działa", main.Status);
            Assert.Single(main.Notes.Visible);
        }

        [Fact]
        public async Task The_live_connection_is_retried_and_the_warning_disappears_once_it_works()
        {
            var main = new MainViewModel(_api, new FakeChanges { StartFailures = 2 }) { RetryDelay = TimeSpan.FromMilliseconds(10) };

            await main.InitializeAsync();

            await WaitUntil(() => main.Status.Length == 0);
        }

        [Fact]
        public async Task A_quick_added_task_switches_to_the_tab_where_it_appears()
        {
            var tasks = new TasksViewModel(_api) { SelectedTab = TaskTab.Upcoming, NewTitle = "bez daty" };
            await tasks.RefreshAsync();

            await tasks.AddAsync();
            await WaitUntil(() => tasks.Items.Count == 1);

            Assert.Equal(TaskTab.NoDate, tasks.SelectedTab);
            Assert.Equal("bez daty", tasks.Items[0].Title);
        }

        [Fact]
        public async Task Unsaved_note_edits_are_saved_on_demand()
        {
            _api.AddNote(new StickyNote { Content = "a" });
            _api.AddNote(new StickyNote { Content = "b" });
            var notes = new NotesViewModel(_api);
            await notes.RefreshAsync();
            notes.Visible[0].Content = "a zmieniona";

            await notes.SaveDirtyAsync();

            Assert.Contains(_api.Notes, n => n.Content == "a zmieniona");
            Assert.All(notes.Visible, n => Assert.False(n.IsDirty));
            Assert.False(notes.HasUnsavedChanges);
        }

        [Fact]
        public async Task Unsaved_changes_are_reported_while_saving_keeps_failing()
        {
            _api.AddNote(new StickyNote { Content = "a" });
            var notes = new NotesViewModel(_api);
            await notes.RefreshAsync();
            notes.Visible[0].Content = "zmieniona";
            _api.Fail = new NotkaApiException("brak połączenia");

            await notes.SaveDirtyAsync();

            Assert.True(notes.HasUnsavedChanges);
            Assert.Equal("brak połączenia", notes.Error);
        }

        [Fact]
        public async Task A_refresh_keeps_existing_note_cards_instead_of_rebuilding_them()
        {
            _api.AddNote(new StickyNote { Content = "pierwsza" });
            var notes = new NotesViewModel(_api);
            await notes.RefreshAsync();
            var first = notes.Visible[0];

            _api.AddNote(new StickyNote { Content = "druga" });
            await notes.RefreshAsync();

            Assert.Equal(2, notes.Visible.Count);
            Assert.Contains(first, notes.Visible);
        }

        [Fact]
        public async Task The_empty_note_text_tells_a_search_miss_from_no_notes()
        {
            var notes = new NotesViewModel(_api);
            await notes.RefreshAsync();
            Assert.Contains("Brak notatek", notes.EmptyText);

            notes.SearchText = "xyz";

            Assert.Contains("nie pasuje", notes.EmptyText);
        }

        private static async Task WaitUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition())
            {
                Assert.True(DateTime.UtcNow < deadline, "Condition was not met in time.");
                await System.Threading.Tasks.Task.Delay(20, TestContext.Current.CancellationToken);
            }
        }

        private sealed class FakeChanges : IChangesClient
        {
            public int StartFailures { get; set; }

            public event Action<ChangeEvent>? TaskChanged;

            public event Action<ChangeEvent>? NoteChanged;

            public event Action? Reconnected;

            public void RaiseTaskChanged() => TaskChanged?.Invoke(new ChangeEvent { Kind = "created", Id = 1 });

            public void RaiseNoteChanged() => NoteChanged?.Invoke(new ChangeEvent { Kind = "created", Id = 2 });

            public void RaiseReconnected() => Reconnected?.Invoke();

            public Task StartAsync(CancellationToken cancellationToken = default)
            {
                if (StartFailures > 0)
                {
                    StartFailures--;
                    return System.Threading.Tasks.Task.FromException(new InvalidOperationException("brak połączenia"));
                }

                return System.Threading.Tasks.Task.CompletedTask;
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private sealed class FakeApi : INotkaApiClient
        {
            private int _nextId = 1;

            public List<TaskItem> Tasks { get; } = [];

            public List<StickyNote> Notes { get; } = [];

            public Exception? Fail { get; set; }

            public void AddTask(TaskItem task)
            {
                task.Id = _nextId++;
                Tasks.Add(task);
            }

            public void AddNote(StickyNote note)
            {
                note.Id = _nextId++;
                note.UpdatedAt = DateTime.Now.AddSeconds(note.Id);
                Notes.Add(note);
            }

            public Task<List<TaskItem>> ListTasksAsync(string filter, CancellationToken cancellationToken = default)
            {
                Throw();
                var today = DateTime.Today;
                var tomorrow = today.AddDays(1);
                var open = Tasks.Where(t => !t.IsDone);
                var result = filter switch
                {
                    "Today" => open.Where(t => t.DueAt >= today && t.DueAt < tomorrow),
                    "Overdue" => open.Where(t => t.DueAt < today),
                    "Upcoming" => open.Where(t => t.DueAt >= tomorrow),
                    "Done" => Tasks.Where(t => t.IsDone),
                    _ => Tasks.AsEnumerable(),
                };
                return System.Threading.Tasks.Task.FromResult(result.ToList());
            }

            public Task<TaskItem> CreateTaskAsync(TaskRequest request, CancellationToken cancellationToken = default)
            {
                Throw();
                var task = new TaskItem { Title = request.Title, DueAt = request.DueAt };
                AddTask(task);
                return System.Threading.Tasks.Task.FromResult(task);
            }

            public Task<TaskItem> CompleteTaskAsync(int id, CancellationToken cancellationToken = default)
            {
                Throw();
                var task = Tasks.Single(t => t.Id == id);
                task.IsDone = true;
                return System.Threading.Tasks.Task.FromResult(task);
            }

            public Task<List<StickyNote>> ListNotesAsync(CancellationToken cancellationToken = default)
            {
                Throw();
                return System.Threading.Tasks.Task.FromResult(Notes.OrderByDescending(n => n.Pinned).ThenByDescending(n => n.UpdatedAt).ToList());
            }

            public Task<StickyNote> CreateNoteAsync(NoteRequest request, CancellationToken cancellationToken = default)
            {
                Throw();
                var note = new StickyNote { Content = request.Content, Color = request.Color };
                AddNote(note);
                return System.Threading.Tasks.Task.FromResult(note);
            }

            public Task<StickyNote> UpdateNoteAsync(int id, NoteRequest request, CancellationToken cancellationToken = default)
            {
                Throw();
                var note = Notes.Single(n => n.Id == id);
                note.Content = request.Content;
                note.Color = request.Color;
                return System.Threading.Tasks.Task.FromResult(note);
            }

            public Task<StickyNote> SetNotePinnedAsync(int id, bool pinned, CancellationToken cancellationToken = default)
            {
                Throw();
                var note = Notes.Single(n => n.Id == id);
                note.Pinned = pinned;
                return System.Threading.Tasks.Task.FromResult(note);
            }

            public Task DeleteNoteAsync(int id, CancellationToken cancellationToken = default)
            {
                Throw();
                Notes.RemoveAll(n => n.Id == id);
                return System.Threading.Tasks.Task.CompletedTask;
            }

            private void Throw()
            {
                if (Fail is not null)
                {
                    throw Fail;
                }
            }
        }
    }
}
