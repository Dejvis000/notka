using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Notka.Desktop
{
    public static class UiFunctions
    {
        private static readonly Dictionary<string, Color> NoteColors = new(StringComparer.OrdinalIgnoreCase)
        {
            ["yellow"] = Color.FromArgb(255, 247, 232, 164),
            ["blue"] = Color.FromArgb(255, 191, 221, 242),
            ["green"] = Color.FromArgb(255, 191, 230, 207),
            ["pink"] = Color.FromArgb(255, 244, 198, 207),
            ["purple"] = Color.FromArgb(255, 214, 204, 240),
        };

        public static Brush NoteBrush(string color) =>
            new SolidColorBrush(NoteColors.TryGetValue(color, out var known) ? known : NoteColors["yellow"]);

        public static bool Not(bool value) => !value;

        public static Visibility VisibleWhen(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

        public static Visibility VisibleWhenText(string? value) => string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;

        public static Visibility VisibleWhenZero(int count) => count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public static bool HasText(string? value) => !string.IsNullOrEmpty(value);

        public static string PinLabel(bool pinned) => pinned ? "Odepnij notatkę" : "Przypnij notatkę na górze";

        public static string PinGlyph(bool pinned) => pinned ? "" : "";
    }
}
