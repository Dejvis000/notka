using System.Runtime.InteropServices;

namespace Notka.Desktop
{
    internal static class ForegroundWindow
    {
        private const int SwRestore = 9;

        public static void Bring(IntPtr window)
        {
            if (IsIconic(window))
            {
                ShowWindow(window, SwRestore);
            }

            if (!TryForeground(window, attach: false) && !TryForeground(window, attach: true))
            {
                SwitchToThisWindow(window, true);
            }
        }

        private static bool TryForeground(IntPtr window, bool attach)
        {
            var current = GetCurrentThreadId();
            var foreground = GetForegroundWindow();
            var foregroundThread = GetWindowThreadProcessId(foreground, out _);
            var attached = attach && foregroundThread != current && !IsHungAppWindow(foreground)
                && AttachThreadInput(current, foregroundThread, true);
            try
            {
                BringWindowToTop(window);
                SetForegroundWindow(window);
            }
            finally
            {
                if (attached)
                {
                    AttachThreadInput(current, foregroundThread, false);
                }
            }

            return GetForegroundWindow() == window;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint thread, uint attachTo, bool attach);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr window);

        [DllImport("user32.dll")]
        private static extern void SwitchToThisWindow(IntPtr window, bool altTab);

        [DllImport("user32.dll")]
        private static extern bool IsHungAppWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);
    }
}
