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
        private const int ShownTolerance = 2;

        public static PixelRect? Compute(
            PixelRect taskbar,
            PixelRect trayNotify,
            PixelRect? widgets,
            PixelRect? lastTaskButton,
            int desiredWidth,
            int height,
            int gap,
            int minWidth,
            PixelRect? monitor = null)
        {
            var horizontal = taskbar.Width > taskbar.Height;
            if (!horizontal || taskbar.Height < MinTaskbarThickness || lastTaskButton is not { } buttons || !IsShown(taskbar, monitor))
            {
                return null;
            }

            var widgetsOnTheRight = widgets is { } w && w.Left > buttons.Right && w.Left < trayNotify.Left;
            var right = (widgetsOnTheRight ? widgets!.Value.Left : trayNotify.Left) - gap;
            var left = buttons.Right + gap;
            var width = Math.Min(desiredWidth, right - left);
            var stripHeight = Math.Min(height, taskbar.Height);
            if (width < Math.Min(minWidth, desiredWidth) || stripHeight <= 0)
            {
                return null;
            }

            var top = taskbar.Top + ((taskbar.Height - stripHeight) / 2);
            return new PixelRect(right - width, top, right, top + stripHeight);
        }

        // An auto-hidden taskbar keeps its height and slides off the monitor.
        private static bool IsShown(PixelRect taskbar, PixelRect? monitor) =>
            monitor is not { } m
            || (taskbar.Left >= m.Left - ShownTolerance && taskbar.Top >= m.Top - ShownTolerance
                && taskbar.Right <= m.Right + ShownTolerance && taskbar.Bottom <= m.Bottom + ShownTolerance);
    }
}
