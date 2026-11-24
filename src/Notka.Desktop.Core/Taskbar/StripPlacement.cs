namespace Notka.Desktop.Taskbar
{
    public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right - Left;

        public int Height => Bottom - Top;
    }

    public static class StripPlacement
    {
        private const int MinTaskbarThickness = 16;

        public static PixelRect? Compute(
            PixelRect taskbar,
            PixelRect trayNotify,
            PixelRect? widgets,
            PixelRect? lastTaskButton,
            int desiredWidth,
            int height,
            int gap,
            int minWidth)
        {
            var horizontal = taskbar.Width > taskbar.Height;
            if (!horizontal || taskbar.Height < MinTaskbarThickness || lastTaskButton is not { } buttons)
            {
                return null;
            }

            var widgetsOnTheRight = widgets is { } w && w.Left > buttons.Right && w.Left < trayNotify.Left;
            var right = (widgetsOnTheRight ? widgets!.Value.Left : trayNotify.Left) - gap;
            var left = buttons.Right + gap;
            var width = Math.Min(desiredWidth, right - left);
            var stripHeight = Math.Min(height, taskbar.Height);
            if (width < minWidth || stripHeight <= 0)
            {
                return null;
            }

            var top = taskbar.Top + ((taskbar.Height - stripHeight) / 2);
            return new PixelRect(right - width, top, right, top + stripHeight);
        }
    }
}
