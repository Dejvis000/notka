using System.Runtime.InteropServices;
using Serilog;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Accessibility;

namespace Notka.Desktop.Taskbar
{
    internal sealed record TaskbarLayout(int? ButtonsRight, PixelRect? Widgets, int? TrayLeft);

    internal sealed class TaskbarProbe : IDisposable
    {
        private const int MaxDepth = 6;
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan BurstInterval = TimeSpan.FromMilliseconds(300);
        private static readonly TimeSpan BurstLength = TimeSpan.FromMilliseconds(2500);
        private static readonly IReadOnlyDictionary<IntPtr, TaskbarLayout> NoLayouts = new Dictionary<IntPtr, TaskbarLayout>();

        private readonly Thread _thread;
        private readonly ManualResetEvent _stop = new(false);
        private readonly AutoResetEvent _poke = new(false);
        private volatile IReadOnlyDictionary<IntPtr, TaskbarLayout> _layouts = NoLayouts;
        private volatile bool _disposed;
        private long _burstUntil;

        public TaskbarProbe()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "NotkaTaskbarProbe" };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        public IReadOnlyDictionary<IntPtr, TaskbarLayout> Layouts => _layouts;

        public void Poke()
        {
            if (!_disposed)
            {
                _poke.Set();
            }
        }

        public void Burst()
        {
            var wasIdle = Environment.TickCount64 >= Interlocked.Read(ref _burstUntil);
            Interlocked.Exchange(ref _burstUntil, Environment.TickCount64 + (long)BurstLength.TotalMilliseconds);
            if (wasIdle)
            {
                Poke();
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _stop.Set();
            if (_thread.Join(TimeSpan.FromSeconds(3)))
            {
                _stop.Dispose();
                _poke.Dispose();
            }
        }

        private static unsafe string Take(BSTR value)
        {
            var text = value.ToString() ?? string.Empty;
            Marshal.FreeBSTR((nint)value.Value);
            return text;
        }

        private static IEnumerable<IntPtr> FindTaskbars()
        {
            var primary = StripNative.FindWindow("Shell_TrayWnd", null);
            if (primary != IntPtr.Zero)
            {
                yield return primary;
            }

            var secondary = IntPtr.Zero;
            while ((secondary = StripNative.FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
            {
                yield return secondary;
            }
        }

        private unsafe void Run()
        {
            PInvoke.CoInitializeEx(null, COINIT.COINIT_MULTITHREADED);
            try
            {
                var automation = (IUIAutomation)new CUIAutomation();
                do
                {
                    _layouts = Scan(automation);
                }
                while (WaitHandle.WaitAny([_stop, _poke], NextWait()) != 0);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Taskbar probe stopped");
                _layouts = NoLayouts;
            }
            finally
            {
                PInvoke.CoUninitialize();
            }
        }

        private TimeSpan NextWait() => Environment.TickCount64 < Interlocked.Read(ref _burstUntil) ? BurstInterval : Interval;

        private static IReadOnlyDictionary<IntPtr, TaskbarLayout> Scan(IUIAutomation automation)
        {
            var layouts = new Dictionary<IntPtr, TaskbarLayout>();
            foreach (var taskbar in FindTaskbars())
            {
                try
                {
                    layouts[taskbar] = ScanTaskbar(automation, taskbar);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Taskbar scan failed");
                    layouts[taskbar] = new TaskbarLayout(null, null, null);
                }
            }

            return layouts;
        }

        private static TaskbarLayout ScanTaskbar(IUIAutomation automation, IntPtr taskbar)
        {
            int? repeaterRight = null;
            int? buttonsRight = null;
            int? startRight = null;
            int? trayLeft = null;
            PixelRect? widgets = null;
            var walker = automation.RawViewWalker;
            Visit(walker, automation.ElementFromHandle(new HWND(taskbar)), 0);
            return new TaskbarLayout(repeaterRight ?? buttonsRight ?? startRight, widgets, trayLeft);

            void Visit(IUIAutomationTreeWalker walker, IUIAutomationElement element, int depth)
            {
                var id = Take(element.CurrentAutomationId);
                var cls = Take(element.CurrentClassName);
                var rect = element.CurrentBoundingRectangle;
                if (id == "TaskbarFrameRepeater" && rect.right > rect.left)
                {
                    repeaterRight = rect.right;
                }
                else if (cls == "Taskbar.TaskListButtonAutomationPeer")
                {
                    buttonsRight = Math.Max(buttonsRight ?? int.MinValue, rect.right);
                }
                else if (id == "StartButton" && rect.right > rect.left)
                {
                    startRight = rect.right;
                }
                else if (id is "SystemTrayIcon" or "NotifyItemIcon" && rect.right > rect.left)
                {
                    trayLeft = Math.Min(trayLeft ?? int.MaxValue, rect.left);
                }
                else if (id == "WidgetsButton" && rect.right > rect.left)
                {
                    widgets = new PixelRect(rect.left, rect.top, rect.right, rect.bottom);
                }

                if (depth >= MaxDepth || cls.Contains("Button", StringComparison.Ordinal))
                {
                    return;
                }

                var child = walker.GetFirstChildElement(element);
                while (child is not null)
                {
                    Visit(walker, child, depth + 1);
                    child = walker.GetNextSiblingElement(child);
                }
            }
        }
    }
}
