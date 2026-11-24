using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Notka.Desktop.Taskbar
{
    internal sealed class StripCardWindow : Window
    {
        private const int Width = 320;
        private const int LineHeight = 24;
        private const int Padding = 16;

        private readonly StackPanel _lines = new() { Spacing = 4 };

        public StripCardWindow()
        {
            SystemBackdrop = new DesktopAcrylicBackdrop();
            var presenter = OverlappedPresenter.Create();
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(true, false);
            AppWindow.SetPresenter(presenter);
            AppWindow.IsShownInSwitchers = false;

            var root = new Grid { Padding = new Thickness(Padding), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(1, 0, 0, 0)) };
            root.Children.Add(_lines);
            root.PointerEntered += (sender, args) => PointerOverChanged?.Invoke(true);
            root.PointerExited += (sender, args) => PointerOverChanged?.Invoke(false);
            Content = root;
        }

        public event Action<bool>? PointerOverChanged;

        public void ShowAbove(PixelRect strip, IReadOnlyList<string> lines, double scale)
        {
            _lines.Children.Clear();
            _lines.Children.Add(new TextBlock { Text = "Dziś", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            if (lines.Count == 0)
            {
                _lines.Children.Add(new TextBlock { Text = "Brak zadań na dziś." });
            }

            foreach (var line in lines)
            {
                _lines.Children.Add(new TextBlock { Text = line, TextTrimming = TextTrimming.CharacterEllipsis });
            }

            var width = (int)Math.Round(Width * scale);
            var rows = Math.Max(lines.Count, 1) + 1;
            var height = (int)Math.Round(((2 * Padding) + (rows * LineHeight)) * scale);
            var gap = (int)Math.Round(8 * scale);
            var x = Math.Max(0, strip.Right - width);
            var y = Math.Max(0, strip.Top - height - gap);
            AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
            AppWindow.Show(false);
        }

        public void HideCard() => AppWindow.Hide();
    }
}
