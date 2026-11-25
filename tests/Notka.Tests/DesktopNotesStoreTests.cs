using Notka.Desktop.Services;

namespace Notka.Tests
{
    public sealed class DesktopNotesStoreTests : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "notka-tests", $"desktop-{Guid.NewGuid():N}.json");

        public void Dispose()
        {
            foreach (var file in new[] { _path, _path + ".tmp" }.Where(File.Exists))
            {
                File.Delete(file);
            }
        }

        [Fact]
        public void Positions_and_the_on_top_flag_survive_a_reload()
        {
            var store = DesktopNotesStore.Load(_path);
            store.Set(3, new DesktopNoteState(100, 200, 300, 280, OnTop: true));
            store.Set(5, new DesktopNoteState(0, 0, 0, 0, OnTop: false));

            var reloaded = DesktopNotesStore.Load(_path);

            Assert.Equal([3, 5], reloaded.Ids.Order());
            Assert.Equal(new DesktopNoteState(100, 200, 300, 280, true), reloaded.Get(3));
            Assert.True(reloaded.Get(3)!.HasPlacement);
            Assert.False(reloaded.Get(5)!.HasPlacement);
        }

        [Fact]
        public void A_removed_note_is_not_restored()
        {
            var store = DesktopNotesStore.Load(_path);
            store.Set(1, new DesktopNoteState(1, 2, 3, 4, false));

            Assert.True(store.Remove(1));
            Assert.False(store.Remove(1));

            Assert.Empty(DesktopNotesStore.Load(_path).Ids);
        }

        [Fact]
        public void Pruning_drops_only_the_ids_that_no_longer_exist()
        {
            var store = DesktopNotesStore.Load(_path);
            store.Set(1, new DesktopNoteState(1, 2, 3, 4, false));
            store.Set(2, new DesktopNoteState(1, 2, 3, 4, false));
            store.Set(3, new DesktopNoteState(1, 2, 3, 4, false));

            var gone = store.Prune([2, 3, 9]);

            Assert.Equal([1], gone);
            Assert.Equal([2, 3], DesktopNotesStore.Load(_path).Ids.Order());
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("{\"1\":null}")]
        [InlineData("[]")]
        public void A_corrupt_file_gives_an_empty_store(string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, content);

            Assert.Empty(DesktopNotesStore.Load(_path).Ids);
        }
    }
}
