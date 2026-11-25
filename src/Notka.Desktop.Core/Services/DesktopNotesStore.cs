using System.Text.Json;

namespace Notka.Desktop.Services
{
    public sealed record DesktopNoteState(int X, int Y, int Width, int Height, bool OnTop)
    {
        public bool HasPlacement => Width > 0 && Height > 0;
    }

    public sealed class DesktopNotesStore(string path)
    {
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

        private readonly string _path = path;
        private readonly Dictionary<int, DesktopNoteState> _items = [];

        public static string DefaultPath() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notka", "desktop-notes.json");

        public IReadOnlyCollection<int> Ids => _items.Keys;

        public static DesktopNotesStore Load(string path)
        {
            var store = new DesktopNotesStore(path);
            try
            {
                if (File.Exists(path) && JsonSerializer.Deserialize<Dictionary<int, DesktopNoteState>>(File.ReadAllText(path), Json) is { } stored)
                {
                    foreach (var (id, state) in stored.Where(p => p.Value is not null))
                    {
                        store._items[id] = state;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
            }

            return store;
        }

        public bool Contains(int id) => _items.ContainsKey(id);

        public DesktopNoteState? Get(int id) => _items.GetValueOrDefault(id);

        public void Set(int id, DesktopNoteState state)
        {
            _items[id] = state;
            Save();
        }

        public bool Remove(int id)
        {
            var removed = _items.Remove(id);
            if (removed)
            {
                Save();
            }

            return removed;
        }

        public IReadOnlyList<int> Prune(IEnumerable<int> existingIds)
        {
            var keep = existingIds.ToHashSet();
            var gone = _items.Keys.Where(id => !keep.Contains(id)).ToList();
            foreach (var id in gone)
            {
                _items.Remove(id);
            }

            if (gone.Count > 0)
            {
                Save();
            }

            return gone;
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
                var temporary = _path + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(_items, Json));
                File.Move(temporary, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
