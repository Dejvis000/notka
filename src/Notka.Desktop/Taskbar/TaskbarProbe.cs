using System.Runtime.InteropServices;
using Serilog;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Accessibility;

namespace Notka.Desktop.Taskbar
{
    internal sealed record TaskbarLayout(int? ButtonsRight, PixelRect? Widgets);

    internal sealed class TaskbarProbe : IDisposable
    {
        private const int MaxDepth = 6;
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

        private readonly Thread _thread;
        private readonly ManualResetEvent _stop = new(false);
        private readonly AutoResetEvent _poke = new(false);
        private volatile TaskbarLayout _layout = new(null, null);
        private volatile bool _disposed;

        public TaskbarProbe()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "NotkaTaskbarProbe" };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        public TaskbarLayout Layout => _layout;

        public void Poke()
        {
            if (!_disposed)
            {
                _poke.Set();
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

        private unsafe void Run()
        {
            PInvoke.CoInitializeEx(null, COINIT.COINIT_MULTITHREADED);
            try
            {
                var automation = (IUIAutomation)new CUIAutomation();
                do
                {
                    _layout = Scan(automation);
                }
                while (WaitHandle.WaitAny([_stop, _poke], Interval) != 0);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Taskbar probe stopped");
                _layout = new TaskbarLayout(null, null);
            }
            finally
            {
                PInvoke.CoUninitialize();
            }
        }

        private static unsafe string Take(BSTR value)
        {
            var text = value.ToString() ?? string.Empty;
            Marshal.FreeBSTR((nint)value.Value);
            return text;
        }

        private static TaskbarLayout Scan(IUIAutomation automation)
        {
            try
            {
                var taskbar = PInvoke.FindWindow("Shell_TrayWnd", null);
                if (taskbar.IsNull)
                {
                    return new TaskbarLayout(null, null);
                }

                int? repeaterRight = null;
                int? buttonsRight = null;
                PixelRect? widgets = null;
                var walker = automation.RawViewWalker;
                Visit(walker, automation.ElementFromHandle(taskbar), 0);
                return new TaskbarLayout(repeaterRight ?? buttonsRight, widgets);

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
            catch (Exception ex)
            {
                Log.Warning(ex, "Taskbar scan failed");
                return new TaskbarLayout(null, null);
            }
        }
    }
}
