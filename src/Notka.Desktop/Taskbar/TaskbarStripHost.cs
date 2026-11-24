using System.Runtime.InteropServices;
using Serilog;

namespace Notka.Desktop.Taskbar
{
    internal sealed class TaskbarStripHost : IDisposable
    {
        private const string ClassName = "NotkaTaskbarStrip";
        private const uint WmUpdate = 0x8001;
        private const uint WmQuit = 0x8002;
        private const uint WmTimer = 0x0113;
        private const uint WmMouseMove = 0x0200;
        private const uint WmLeftButtonUp = 0x0202;
        private const uint WmMouseLeave = 0x02A3;
        private const uint WmDestroy = 0x0002;
        private const uint TickMilliseconds = 1000;

        private static readonly StripNative.WindowProcedure Procedure = WindowProc;
        private static TaskbarStripHost? s_instance;

        private readonly Thread _thread;
        private readonly ManualResetEventSlim _ready = new();
        private readonly TaskbarProbe _probe = new();
        private readonly PlacementTracker _tracker = new();
        private readonly object _gate = new();
        private Snapshot _snapshot = new(StripContent.Unavailable, false, false);
        private PixelRect? _bounds;

        private IntPtr _window;
        private StripRenderer? _renderer;
        private bool _hover;
        private bool _trackingMouse;
        private bool _visible;
        private bool _wasEnabled;
        private StripState? _reportedState;
        private PixelRect? _placed;
        private string? _renderedKey;

        public TaskbarStripHost()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "NotkaTaskbarStrip" };
            _thread.SetApartmentState(ApartmentState.STA);
        }

        public event Action? Clicked;

        public event Action<bool>? HoverChanged;

        public event Action<StripState>? StateChanged;

        public double Scale { get; private set; } = 1.0;

        public PixelRect? Bounds
        {
            get
            {
                lock (_gate)
                {
                    return _bounds;
                }
            }
        }

        private sealed record Snapshot(StripContent Content, bool LightTheme, bool Enabled);

        public void Start()
        {
            _thread.Start();
            _ready.Wait(TimeSpan.FromSeconds(3));
        }

        public void Update(StripContent content, bool lightTheme)
        {
            lock (_gate)
            {
                _snapshot = _snapshot with { Content = content, LightTheme = lightTheme };
            }

            Post(WmUpdate);
        }

        public void SetEnabled(bool enabled)
        {
            lock (_gate)
            {
                _snapshot = _snapshot with { Enabled = enabled };
            }

            Post(WmUpdate);
        }

        public void Dispose()
        {
            Post(WmQuit);
            var stopped = _thread.Join(TimeSpan.FromSeconds(3));
            _probe.Dispose();
            if (stopped)
            {
                _ready.Dispose();
            }
        }

        private static IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
        {
            var host = s_instance;
            if (host is not null && host._window == window)
            {
                try
                {
                    switch (message)
                    {
                        case WmTimer:
                            host.Apply(force: false);
                            return IntPtr.Zero;
                        case WmUpdate:
                            host._probe.Poke();
                            host.Apply(force: true);
                            return IntPtr.Zero;
                        case WmQuit:
                            StripNative.DestroyWindow(window);
                            return IntPtr.Zero;
                        case WmMouseMove:
                            host.OnMouseMove();
                            return IntPtr.Zero;
                        case WmMouseLeave:
                            host.SetHover(false);
                            return IntPtr.Zero;
                        case WmLeftButtonUp:
                            host.Clicked?.Invoke();
                            return IntPtr.Zero;
                        case WmDestroy:
                            StripNative.KillTimer(window, 1);
                            StripNative.PostQuitMessage(0);
                            return IntPtr.Zero;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Taskbar strip message {Message} failed", message);
                    return IntPtr.Zero;
                }
            }

            return StripNative.DefaultWindowProcedure(window, message, wParam, lParam);
        }

        private static bool IsFullScreenBusy()
        {
            if (StripNative.SHQueryUserNotificationState(out var state) < 0)
            {
                return false;
            }

            return state is 2 or 3 or 4;
        }

        private void Run()
        {
            s_instance = this;
            _renderer = new StripRenderer();
            var instance = Marshal.GetHINSTANCE(typeof(TaskbarStripHost).Module);
            var windowClass = new StripNative.WindowClass
            {
                Size = (uint)Marshal.SizeOf<StripNative.WindowClass>(),
                Procedure = Marshal.GetFunctionPointerForDelegate(Procedure),
                Instance = instance,
                Cursor = StripNative.LoadCursor(IntPtr.Zero, new IntPtr(StripNative.IdcArrow)),
                ClassName = ClassName,
            };
            StripNative.RegisterClass(ref windowClass);
            _window = StripNative.CreateWindow(
                StripNative.WsExLayered | StripNative.WsExNoActivate | StripNative.WsExToolWindow | StripNative.WsExTopmost,
                ClassName,
                "Notka",
                StripNative.WsPopup,
                0,
                0,
                1,
                1,
                IntPtr.Zero,
                IntPtr.Zero,
                instance,
                IntPtr.Zero);
            StripNative.SetTimer(_window, 1, TickMilliseconds, IntPtr.Zero);
            _ready.Set();

            while (StripNative.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                StripNative.TranslateMessage(ref message);
                StripNative.DispatchMessage(ref message);
            }

            _renderer.Dispose();
            s_instance = null;
        }

        private void OnMouseMove()
        {
            if (!_trackingMouse)
            {
                var track = new StripNative.TrackMouseEvent
                {
                    Size = (uint)Marshal.SizeOf<StripNative.TrackMouseEvent>(),
                    Flags = StripNative.TmeLeave,
                    Window = _window,
                };
                StripNative.TrackMouse(ref track);
                _trackingMouse = true;
            }

            SetHover(true);
        }

        private void SetHover(bool hover)
        {
            if (!hover)
            {
                _trackingMouse = false;
            }

            if (_hover == hover)
            {
                return;
            }

            _hover = hover;
            Apply(force: true);
            if (_hover == hover)
            {
                HoverChanged?.Invoke(hover);
            }
        }

        private void Apply(bool force)
        {
            Snapshot snapshot;
            lock (_gate)
            {
                snapshot = _snapshot;
            }

            var target = Measure(snapshot, out var scale, out var taskbarWindow);
            var active = snapshot.Enabled && !IsFullScreenBusy();
            if (snapshot.Enabled && !_wasEnabled)
            {
                _reportedState = null;
            }

            _wasEnabled = snapshot.Enabled;
            if (active)
            {
                _tracker.Report(target is not null);
                if (_reportedState != _tracker.State)
                {
                    _reportedState = _tracker.State;
                    StateChanged?.Invoke(_tracker.State);
                }
            }

            if (!active || target is not { } rect)
            {
                Hide();
                return;
            }

            var key = $"{snapshot.Content.Text}|{snapshot.LightTheme}|{_hover}|{rect.Width}|{rect.Height}|{scale}";
            if (force || key != _renderedKey || !_visible)
            {
                _renderer!.Render(snapshot.Content.Text, snapshot.LightTheme, _hover, rect.Width, rect.Height, scale);
                _renderedKey = key;
                Present(rect);
            }
            else if (_placed != rect)
            {
                StripNative.SetWindowPos(_window, StripNative.HwndTopmost, rect.Left, rect.Top, 0, 0, StripNative.SwpNoSize | StripNative.SwpNoActivate);
            }

            _placed = rect;
            Scale = scale;
            SetBounds(rect);
            KeepAboveTaskbar(taskbarWindow);
        }

        private PixelRect? Measure(Snapshot snapshot, out double scale, out IntPtr taskbarWindow)
        {
            scale = 1.0;
            taskbarWindow = StripNative.FindWindow("Shell_TrayWnd", null);
            if (taskbarWindow == IntPtr.Zero || !StripNative.GetWindowRect(taskbarWindow, out var taskbarRect))
            {
                return null;
            }

            var tray = StripNative.FindWindowEx(taskbarWindow, IntPtr.Zero, "TrayNotifyWnd", null);
            if (tray == IntPtr.Zero || !StripNative.GetWindowRect(tray, out var trayRect))
            {
                return null;
            }

            scale = StripNative.GetDpiForWindow(taskbarWindow) / 96.0;
            var layout = _probe.Layout;
            var taskbar = new PixelRect(taskbarRect.Left, taskbarRect.Top, taskbarRect.Right, taskbarRect.Bottom);
            PixelRect? buttons = layout.ButtonsRight is { } right ? new PixelRect(taskbar.Left, taskbar.Top, right, taskbar.Bottom) : null;
            var padding = (int)Math.Round(12 * scale);
            var desiredWidth = Math.Min(_renderer!.MeasureTextWidth(snapshot.Content.Text, scale) + (2 * padding), (int)(420 * scale));
            var height = Math.Min((int)Math.Round(36 * scale), taskbar.Height - (int)Math.Round(8 * scale));
            return StripPlacement.Compute(
                taskbar,
                new PixelRect(trayRect.Left, trayRect.Top, trayRect.Right, trayRect.Bottom),
                layout.Widgets,
                buttons,
                desiredWidth,
                height,
                (int)Math.Round(8 * scale),
                (int)Math.Round(120 * scale));
        }

        private void Present(PixelRect rect)
        {
            var screen = StripNative.GetDC(IntPtr.Zero);
            var position = new StripNative.Point { X = rect.Left, Y = rect.Top };
            var size = new StripNative.Size { Width = rect.Width, Height = rect.Height };
            var source = new StripNative.Point();
            var blend = new StripNative.BlendFunction { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            StripNative.UpdateLayeredWindow(_window, screen, ref position, ref size, _renderer!.DcHandle, ref source, 0, ref blend, StripNative.UlwAlpha);
            StripNative.ReleaseDC(IntPtr.Zero, screen);
            if (!_visible)
            {
                StripNative.ShowWindow(_window, StripNative.SwShowNoActivate);
                _visible = true;
            }
        }

        private void Hide()
        {
            if (_visible)
            {
                StripNative.ShowWindow(_window, StripNative.SwHide);
                _visible = false;
            }

            _placed = null;
            SetBounds(null);
            _renderedKey = null;
            if (_hover)
            {
                _hover = false;
                _trackingMouse = false;
                HoverChanged?.Invoke(false);
            }
        }

        private void SetBounds(PixelRect? bounds)
        {
            lock (_gate)
            {
                _bounds = bounds;
            }
        }

        private void KeepAboveTaskbar(IntPtr taskbarWindow)
        {
            for (var above = StripNative.GetWindow(taskbarWindow, StripNative.GwHwndPrev); above != IntPtr.Zero; above = StripNative.GetWindow(above, StripNative.GwHwndPrev))
            {
                if (above == _window)
                {
                    return;
                }
            }

            StripNative.SetWindowPos(_window, StripNative.HwndTopmost, 0, 0, 0, 0, StripNative.SwpNoMove | StripNative.SwpNoSize | StripNative.SwpNoActivate);
        }

        private void Post(uint message)
        {
            if (_window != IntPtr.Zero)
            {
                StripNative.PostMessage(_window, message, IntPtr.Zero, IntPtr.Zero);
            }
        }
    }
}
