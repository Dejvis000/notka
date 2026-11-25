namespace Notka.Desktop.Taskbar
{
    public static class CardPlacement
    {
        public static (int X, int Y) Compute(PixelRect strip, PixelRect workArea, int width, int height, int gap)
        {
            var below = strip.Top < workArea.Top;
            var y = below ? strip.Bottom + gap : strip.Top - height - gap;
            var x = strip.Right - width;
            return (
                Math.Max(workArea.Left, Math.Min(x, workArea.Right - width)),
                Math.Max(workArea.Top, Math.Min(y, workArea.Bottom - height)));
        }
    }
}
