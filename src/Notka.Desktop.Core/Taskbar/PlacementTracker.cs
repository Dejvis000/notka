namespace Notka.Desktop.Taskbar
{
    public enum StripState
    {
        Starting,
        Visible,
        Unavailable,
    }

    public sealed class PlacementTracker(int failuresBeforeFallback = 3)
    {
        private readonly int _failuresBeforeFallback = failuresBeforeFallback;
        private int _failures;

        public StripState State { get; private set; } = StripState.Starting;

        public bool Report(bool placed)
        {
            var before = State;
            if (placed)
            {
                _failures = 0;
                State = StripState.Visible;
            }
            else if (++_failures >= _failuresBeforeFallback)
            {
                State = StripState.Unavailable;
            }

            return State != before;
        }
    }
}
