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
        private const uint TickMilliseconds = 250;
        private const uint ZOrderMilliseconds = 20;

        private static readonly StripNative.WindowProcedure Procedure = WindowProc;
        private static readonly StripNative.WinEventProcedure EventProcedure = OnWinEvent;
        private static TaskbarStripHost? s_instance;

        private readonly Thread _thread;
        private readonly ManualResetEventSlim _ready = new();
        private readonly TaskbarProbe _probe = new();
        private readonly PlacementTracker _tracker = new(failuresBeforeFallback: 3000 / (int)TickMilliseconds);
        private readonly object _gate = new();
        private readonly Dictionary<IntPtr, Surface> _surfaces = [];
        private readonly Dictionary<IntPtr, Surface> _byWindow = [];
        private Snapshot _snapshot = new(StripContent.Unavailable, false, false, StripTargets.Primary, true);
        private PixelRect? _bounds;
        private double _scale = 1.0;

        private IntPtr _hub;
        private IntPtr _hook;
        private StripRenderer? _measurer;
        private Surface? _lastHover;
        private bool _wasEnabled;
        private StripState? _reportedState;

        public TaskbarStripHost()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "NotkaTaskbarStrip" };
            _thread.SetApartmentState(ApartmentState.STA);
        }

        public event Action? Clicked;

        public event Action<bool>? HoverChanged;

        public event Action<StripState>? StateChanged;

        public double Scale
        {
            get
            {
                lock (_gate)
                {
                    return _scale;
                }
            }
        }

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

        private sealed record Snapshot(StripContent Content, bool LightTheme, bool Enabled, string Target, bool Background);

        private sealed class Surface(IntPtr taskbar, IntPtr window, StripRenderer renderer)
        {
            public IntPtr Taskbar { get; } = taskbar;

            public IntPtr Window { get; } = window;

            public StripRenderer Renderer { get; } = renderer;

            public bool Hover { get; set; }

            public bool TrackingMouse { get; set; }

            public bool Visible { get; set; }

            public PixelRect? Placed { get; set; }

            public double Scale { get; set; } = 1.0;

            public string? RenderedKey { get; set; }

            public PixelRect? PendingMove { get; set; }

            public long PendingSince { get; set; }

            public long FailingSince { get; set; }
        }

        public void Start()
        {
            _thread.Start();
            _ready.Wait(TimeSpan.FromSeconds(3));
        }

        public void Update(StripContent content, bool lightTheme)
        {
            Change(snapshot => snapshot with { Content = content, LightTheme = lightTheme });
        }

        public void SetEnabled(bool enabled) => Change(snapshot => snapshot with { Enabled = enabled });

        public void SetBackground(bool background) => Change(snapshot => snapshot with { Background = background });

        public void SetTarget(string target) => Change(snapshot => snapshot with { Target = target });

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
            if (host is not null)
            {
                try
                {
                    if (host._hub == window)
                    {
                        switch (message)
                        {
                            case WmTimer when wParam == 2:
                                foreach (var surface in host._surfaces.Values.Where(s => s.Visible))
                                {
                                    host.KeepAboveTaskbar(surface);
                                }

                                return IntPtr.Zero;
                            case WmTimer:
                                host.Apply(force: false);
                                return IntPtr.Zero;
                            case WmUpdate:
                                host._probe.Poke();
                                host.Apply(force: true);
                                return IntPtr.Zero;
                            case WmQuit:
                                host.DestroySurfaces();
                                StripNative.DestroyWindow(window);
                                return IntPtr.Zero;
                            case WmDestroy:
                                StripNative.KillTimer(window, 1);
                                StripNative.KillTimer(window, 2);
                                StripNative.PostQuitMessage(0);
                                return IntPtr.Zero;
                        }
                    }
                    else if (host._byWindow.TryGetValue(window, out var surface))
                    {
                        switch (message)
                        {
                            case WmMouseMove:
                                host.OnMouseMove(surface);
                                return IntPtr.Zero;
                            case WmMouseLeave:
                                host.SetHover(surface, false);
                                return IntPtr.Zero;
                            case WmLeftButtonUp:
                                host.Clicked?.Invoke();
                                return IntPtr.Zero;
                        }
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

        private static void OnWinEvent(IntPtr hook, uint eventId, IntPtr window, int objectId, int childId, uint thread, uint time)
        {
            if (objectId == 0 && window != IntPtr.Zero && IsTaskbarCandidate(window))
            {
                s_instance?._probe.Burst();
            }
        }

        private static bool IsTaskbarCandidate(IntPtr window) =>
            StripNative.GetAncestor(window, StripNative.GaRoot) == window
            && StripNative.GetWindow(window, StripNative.GwOwner) == IntPtr.Zero
            && (StripNative.GetWindowLong(window, StripNative.GwlExStyle) & StripNative.WsExToolWindow) == 0;

        private static bool IsFullScreenBusy()
        {
            if (StripNative.SHQueryUserNotificationState(out var state) < 0)
            {
                return false;
            }

            return state is 2 or 3 or 4;
        }

        private void Change(Func<Snapshot, Snapshot> change)
        {
            lock (_gate)
            {
                _snapshot = change(_snapshot);
            }

            Post(WmUpdate);
        }

        private void Run()
        {
            s_instance = this;
            _measurer = new StripRenderer();
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
            _hub = StripNative.CreateWindow(0, ClassName, "Notka", 0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, instance, IntPtr.Zero);
            StripNative.SetTimer(_hub, 1, TickMilliseconds, IntPtr.Zero);
            _hook = StripNative.SetWinEventHook(StripNative.EventObjectShow,StripNative.EventObjectHide, IntPtr.Zero, EventProcedure, 0, 0, StripNative.WineventOutOfContext);
            _ready.Set();

            while (StripNative.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                StripNative.TranslateMessage(ref message);
                StripNative.DispatchMessage(ref message);
            }

            if (_hook != IntPtr.Zero)
            {
                StripNative.UnhookWinEvent(_hook);
            }

            _measurer.Dispose();
            s_instance = null;
        }

        private void OnMouseMove(Surface surface)
        {
            if (!surface.TrackingMouse)
            {
                var track = new StripNative.TrackMouseEvent
                {
                    Size = (uint)Marshal.SizeOf<StripNative.TrackMouseEvent>(),
                    Flags = StripNative.TmeLeave,
                    Window = surface.Window,
                };
                StripNative.TrackMouse(ref track);
                surface.TrackingMouse = true;
            }

            SetHover(surface, true);
        }

        private void SetHover(Surface surface, bool hover)
        {
            if (!hover)
            {
                surface.TrackingMouse = false;
            }

            if (surface.Hover == hover)
            {
                return;
            }

            surface.Hover = hover;
            if (hover)
            {
                _lastHover = surface;
            }

            Apply(force: false);
            if (surface.Hover == hover)
            {
                HoverChanged?.Invoke(hover);
            }
        }

        private const int MoveSettleMilliseconds = 450;
        private const int HideGraceMilliseconds = 1500;

        private void Apply(bool force)
        {
            Snapshot snapshot;
            lock (_gate)
            {
                snapshot = _snapshot;
            }

            var active = snapshot.Enabled && !IsFullScreenBusy();
            if (snapshot.Enabled && !_wasEnabled)
            {
                _reportedState = null;
            }

            _wasEnabled = snapshot.Enabled;
            var anyPlaced = false;
            var anySlidOut = false;
            var anyShownFailed = false;
            if (active)
            {
                var wanted = new HashSet<IntPtr>();
                var primary = StripNative.FindWindow("Shell_TrayWnd", null);
                var taskbars = _probe.Layouts.Keys.ToList();
                if (primary != IntPtr.Zero && !taskbars.Contains(primary))
                {
                    taskbars.Insert(0, primary);
                }

                var target = snapshot.Target;
                if (target is not (StripTargets.Primary or StripTargets.All) && !taskbars.Any(t => StripNative.DeviceOf(t) == target))
                {
                    target = StripTargets.Primary;
                }

                foreach (var taskbar in taskbars)
                {
                    var chosen = target switch
                    {
                        StripTargets.All => true,
                        StripTargets.Primary => taskbar == primary,
                        _ => StripNative.DeviceOf(taskbar) == target,
                    };
                    if (!chosen)
                    {
                        continue;
                    }

                    wanted.Add(taskbar);
                    if (Measure(snapshot, taskbar, taskbar == primary, out var scale) is { } rect)
                    {
                        Place(GetSurface(taskbar), rect, scale, snapshot, force);
                        anyPlaced = true;
                    }
                    else
                    {
                        if (IsSlidOut(taskbar))
                        {
                            anySlidOut = true;
                        }
                        else
                        {
                            anyShownFailed = true;
                        }

                        if (_surfaces.TryGetValue(taskbar, out var unplaced))
                        {
                            var now = Environment.TickCount64;
                            unplaced.FailingSince = unplaced.FailingSince == 0 ? now : unplaced.FailingSince;
                            if (unplaced.Visible && !anySlidOut && now - unplaced.FailingSince < HideGraceMilliseconds)
                            {
                                anyPlaced = true;
                            }
                            else
                            {
                                Hide(unplaced);
                            }
                        }
                    }
                }

                foreach (var gone in _surfaces.Keys.Where(t => !wanted.Contains(t)).ToList())
                {
                    Destroy(_surfaces[gone]);
                }

                var allHidden = !anyPlaced && !anyShownFailed && anySlidOut;
                if (!allHidden)
                {
                    _tracker.Report(anyPlaced);
                }

                var state = allHidden ? StripState.Hidden : _tracker.State;
                if (_reportedState != state)
                {
                    _reportedState = state;
                    StateChanged?.Invoke(state);
                }
            }
            else
            {
                foreach (var surface in _surfaces.Values)
                {
                    Hide(surface);
                }
            }

            PublishBounds();
        }

        private PixelRect? Measure(Snapshot snapshot, IntPtr taskbarWindow, bool primary, out double scale)
        {
            scale = 1.0;
            if (!StripNative.GetWindowRect(taskbarWindow, out var taskbarRect))
            {
                return null;
            }

            var layout = _probe.Layouts.TryGetValue(taskbarWindow, out var scanned) ? scanned : new TaskbarLayout(null, null, null);

            var taskbar = new PixelRect(taskbarRect.Left, taskbarRect.Top, taskbarRect.Right, taskbarRect.Bottom);
            var trayLeft = layout.TrayLeft;
            if (trayLeft is null && primary)
            {
                var tray = StripNative.FindWindowEx(taskbarWindow, IntPtr.Zero, "TrayNotifyWnd", null);
                if (tray != IntPtr.Zero && StripNative.GetWindowRect(tray, out var trayRect))
                {
                    trayLeft = trayRect.Left;
                }
            }

            if (trayLeft is not { } trayEdge)
            {
                return null;
            }

            scale = StripNative.GetDpiForWindow(taskbarWindow) / 96.0;
            PixelRect? buttons = layout.ButtonsRight is { } right ? new PixelRect(taskbar.Left, taskbar.Top, right, taskbar.Bottom) : null;
            var padding = (int)Math.Round(12 * scale);
            var desiredWidth = Math.Min(_measurer!.MeasureTextWidth(snapshot.Content.Text, scale) + (2 * padding), (int)(420 * scale));
            var height = Math.Min((int)Math.Round(36 * scale), taskbar.Height - (int)Math.Round(8 * scale));
            return StripPlacement.Compute(
                taskbar,
                new PixelRect(trayEdge, taskbar.Top, taskbar.Right, taskbar.Bottom),
                layout.Widgets,
                buttons,
                desiredWidth,
                height,
                (int)Math.Round(8 * scale),
                (int)Math.Round(120 * scale),
                MonitorRect(taskbarWindow));
        }

        private static bool IsSlidOut(IntPtr taskbar) =>
            StripNative.GetWindowRect(taskbar, out var rect)
            && MonitorRect(taskbar) is { } monitor
            && (rect.Bottom > monitor.Bottom + 2 || rect.Top < monitor.Top - 2 || rect.Right > monitor.Right + 2 || rect.Left < monitor.Left - 2);

        private static PixelRect? MonitorRect(IntPtr window) =>
            StripNative.InfoOf(StripNative.MonitorFromWindow(window, StripNative.MonitorDefaultToNearest)) is { } info
                ? new PixelRect(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom)
                : null;

        private Surface GetSurface(IntPtr taskbar)
        {
            if (_surfaces.TryGetValue(taskbar, out var existing))
            {
                return existing;
            }

            var window = StripNative.CreateWindow(
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
                Marshal.GetHINSTANCE(typeof(TaskbarStripHost).Module),
                IntPtr.Zero);
            var surface = new Surface(taskbar, window, new StripRenderer());
            _surfaces[taskbar] = surface;
            _byWindow[window] = surface;
            return surface;
        }

        private void Place(Surface surface, PixelRect rect, double scale, Snapshot snapshot, bool force)
        {
            var key = $"{snapshot.Content.Text}|{snapshot.LightTheme}|{surface.Hover}|{snapshot.Background}|{rect.Width}|{rect.Height}|{scale}";
            if (force || key != surface.RenderedKey || !surface.Visible)
            {
                surface.Renderer.Render(snapshot.Content.Text, snapshot.LightTheme, surface.Hover, snapshot.Background, rect.Width, rect.Height, scale);
                surface.RenderedKey = key;
                Log.Information("Strip render at {Rect} (force {Force}, visible {Visible}, hover {Hover})", rect, force, surface.Visible, surface.Hover);
                Present(surface, rect);
                surface.Placed = rect;
                surface.PendingMove = null;
            }
            else if (surface.Placed is { } placed && placed != rect && (rect.Top != placed.Top || Math.Abs(rect.Left - placed.Left) >= (int)Math.Round(24 * scale)) && MoveSettled(surface, rect))
            {
                Log.Information("Strip move {From} -> {To}", surface.Placed, rect);
                StripNative.SetWindowPos(surface.Window, StripNative.HwndTopmost, rect.Left, rect.Top, 0, 0, StripNative.SwpNoSize | StripNative.SwpNoActivate);
                surface.Placed = rect;
                surface.PendingMove = null;
            }

            surface.FailingSince = 0;
            surface.Scale = scale;
            KeepAboveTaskbar(surface);
        }

        private static bool MoveSettled(Surface surface, PixelRect rect)
        {
            var now = Environment.TickCount64;
            if (surface.PendingMove != rect)
            {
                surface.PendingMove = rect;
                surface.PendingSince = now;
                return false;
            }

            return now - surface.PendingSince >= MoveSettleMilliseconds;
        }

        private void Present(Surface surface, PixelRect rect)
        {
            var screen = StripNative.GetDC(IntPtr.Zero);
            var position = new StripNative.Point { X = rect.Left, Y = rect.Top };
            var size = new StripNative.Size { Width = rect.Width, Height = rect.Height };
            var source = new StripNative.Point();
            var blend = new StripNative.BlendFunction { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            StripNative.UpdateLayeredWindow(surface.Window, screen, ref position, ref size, surface.Renderer.DcHandle, ref source, 0, ref blend, StripNative.UlwAlpha);
            StripNative.ReleaseDC(IntPtr.Zero, screen);
            if (!surface.Visible)
            {
                StripNative.ShowWindow(surface.Window, StripNative.SwShowNoActivate);
                surface.Visible = true;
                UpdateZOrderTimer();
            }
        }

        private void UpdateZOrderTimer()
        {
            if (_surfaces.Values.Any(s => s.Visible))
            {
                StripNative.SetTimer(_hub, 2, ZOrderMilliseconds, IntPtr.Zero);
            }
            else
            {
                StripNative.KillTimer(_hub, 2);
            }
        }

        private void Hide(Surface surface)
        {
            if (surface.Visible)
            {
                Log.Information("Strip hidden (fullscreen busy {Busy})", IsFullScreenBusy());
                StripNative.ShowWindow(surface.Window, StripNative.SwHide);
                surface.Visible = false;
                UpdateZOrderTimer();
            }

            surface.Placed = null;
            surface.RenderedKey = null;
            if (surface.Hover)
            {
                surface.Hover = false;
                surface.TrackingMouse = false;
                HoverChanged?.Invoke(false);
            }
        }

        private void Destroy(Surface surface)
        {
            Hide(surface);
            _surfaces.Remove(surface.Taskbar);
            _byWindow.Remove(surface.Window);
            if (_lastHover == surface)
            {
                _lastHover = null;
            }

            StripNative.DestroyWindow(surface.Window);
            surface.Renderer.Dispose();
        }

        private void DestroySurfaces()
        {
            foreach (var surface in _surfaces.Values.ToList())
            {
                Destroy(surface);
            }

            PublishBounds();
        }

        private void PublishBounds()
        {
            var shown = _lastHover is { Placed: not null } hovered
                ? hovered
                : _surfaces.Values.FirstOrDefault(s => s.Placed is not null);
            lock (_gate)
            {
                _bounds = shown?.Placed;
                _scale = shown?.Scale ?? 1.0;
            }
        }

        private void KeepAboveTaskbar(Surface surface)
        {
            for (var above = StripNative.GetWindow(surface.Taskbar, StripNative.GwHwndPrev); above != IntPtr.Zero; above = StripNative.GetWindow(above, StripNative.GwHwndPrev))
            {
                if (above == surface.Window)
                {
                    return;
                }
            }

            var directlyAbove = StripNative.GetWindow(surface.Taskbar, StripNative.GwHwndPrev);
            StripNative.SetWindowPos(surface.Window, directlyAbove != IntPtr.Zero ? directlyAbove : StripNative.HwndTopmost, 0, 0, 0, 0, StripNative.SwpNoMove | StripNative.SwpNoSize | StripNative.SwpNoActivate);
        }

        private void Post(uint message)
        {
            if (_hub != IntPtr.Zero)
            {
                StripNative.PostMessage(_hub, message, IntPtr.Zero, IntPtr.Zero);
            }
        }
    }
}
