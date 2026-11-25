using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Notka.Core;

namespace Notka.Desktop.ViewModels
{
    public partial class NoteViewModel : ObservableObject
    {
        private readonly NotesViewModel _owner;
        private bool _updating;

        public NoteViewModel(StickyNote note, NotesViewModel owner)
        {
            _owner = owner;
            Id = note.Id;
            Update(note);
        }

        public int Id { get; }

        [ObservableProperty]
        private string _content = string.Empty;

        [ObservableProperty]
        private string _color = "yellow";

        [ObservableProperty]
        private bool _pinned;

        [ObservableProperty]
        private string _updatedText = string.Empty;

        [ObservableProperty]
        private bool _isDirty;

        internal void Update(StickyNote note)
        {
            _updating = true;
            if (!IsDirty)
            {
                Content = note.Content;
            }

            Color = note.Color;
            Pinned = note.Pinned;
            UpdatedText = note.UpdatedAt.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture);
            _updating = false;
        }

        public override string ToString() => Content;

        [RelayCommand]
        private Task SaveAsync() => _owner.SaveAsync(this);

        [RelayCommand]
        private Task TogglePinAsync() => _owner.SetPinnedAsync(this, !Pinned);

        [RelayCommand]
        private Task DeleteAsync() => _owner.DeleteAsync(this);

        [RelayCommand]
        private Task SetColorAsync(string color) => _owner.SetColorAsync(this, color);

        partial void OnContentChanged(string value)
        {
            if (!_updating)
            {
                IsDirty = true;
            }
        }
    }
}
