using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Notka.Desktop.Services;
using Notka.Desktop.ViewModels;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Notka.Desktop
{
    public sealed partial class DesktopNoteWindow : Window
    {
        private const int DefaultWidth = 300;
        private const int DefaultHeight = 320;
        private const int MinimumWidth = 200;
        private const int MinimumHeight = 160;

        private static readonly Windows.UI.Color Ink = Windows.UI.Color.FromArgb(255, 0x2B, 0x2B, 0x2B);

        private readonly Action<DesktopNoteState> _persist;
        private readonly DispatcherQueueTimer _saveTimer;
        private readonly DispatcherQueueTimer _placeTimer;

        public DesktopNoteWindow(NoteViewModel note, DesktopNoteState? state, int cascade, Action<DesktopNoteState> persist)
        {
            Note = note;
            _persist = persist;
            InitializeComponent();
            Title = "Notatka";
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(DragArea);
            StyleCaptionButtons();

            var scale = Scale;
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.IsAlwaysOnTop = state?.OnTop ?? false;
                presenter.PreferredMinimumWidth = (int)(MinimumWidth * scale);
                presenter.PreferredMinimumHeight = (int)(MinimumHeight * scale);
            }

            OnTopToggle.IsChecked = state?.OnTop ?? false;
            Place(state, cascade, scale);

            var queue = DispatcherQueue.GetForCurrentThread();
            _saveTimer = queue.CreateTimer();
            _saveTimer.Interval = TimeSpan.FromMilliseconds(1500);
            _saveTimer.IsRepeating = false;
            _saveTimer.Tick += (_, _) => SaveNow();
            _placeTimer = queue.CreateTimer();
            _placeTimer.Interval = TimeSpan.FromMilliseconds(500);
            _placeTimer.IsRepeating = false;
            _placeTimer.Tick += (_, _) => Persist();

            AppWindow.Changed += OnAppWindowChanged;
            _placeTimer.Start();
            Activated += OnActivated;
            Closed += (_, _) =>
            {
                _saveTimer.Stop();
                _placeTimer.Stop();
                SaveNow();
                Bindings.StopTracking();
            };
        }

        public NoteViewModel Note { get; }

        public void Bring()
        {
            AppWindow.Show();
            Activate();
            ForegroundWindow.Bring(WinRT.Interop.WindowNative.GetWindowHandle(this));
        }

        private double Scale => PInvoke.GetDpiForWindow(new HWND(WinRT.Interop.WindowNative.GetWindowHandle(this))) / 96.0;

        private void StyleCaptionButtons()
        {
            var bar = AppWindow.TitleBar;
            bar.ButtonBackgroundColor = Colors.Transparent;
            bar.ButtonInactiveBackgroundColor = Colors.Transparent;
            bar.ButtonForegroundColor = Ink;
            bar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 0x70, 0x70, 0x70);
            bar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(0x22, 0, 0, 0);
            bar.ButtonHoverForegroundColor = Ink;
            bar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(0x33, 0, 0, 0);
            bar.ButtonPressedForegroundColor = Ink;
        }

        private void Place(DesktopNoteState? state, int cascade, double scale)
        {
            if (state is { HasPlacement: true } saved)
            {
                var rect = new RectInt32(saved.X, saved.Y, saved.Width, saved.Height);
                if (DisplayArea.GetFromRect(rect, DisplayAreaFallback.None) is not null)
                {
                    AppWindow.MoveAndResize(rect);
                    return;
                }
            }

            var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            var width = (int)(DefaultWidth * scale);
            var height = (int)(DefaultHeight * scale);
            var offset = (int)(36 * scale) * (cascade % 8);
            AppWindow.MoveAndResize(new RectInt32(
                Math.Max(work.X, work.X + work.Width - width - (int)(40 * scale) - offset),
                work.Y + (int)(80 * scale) + offset,
                width,
                height));
        }

        private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (args.DidPositionChange || args.DidSizeChange)
            {
                _placeTimer.Stop();
                _placeTimer.Start();
            }
        }

        private void OnActivated(object sender, WindowActivatedEventArgs args)
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                SaveNow();
            }
        }

        private void Persist()
        {
            if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized })
            {
                return;
            }

            var position = AppWindow.Position;
            var size = AppWindow.Size;
            var onTop = AppWindow.Presenter is OverlappedPresenter { IsAlwaysOnTop: true };
            _persist(new DesktopNoteState(position.X, position.Y, size.Width, size.Height, onTop));
        }

        private void SaveNow()
        {
            if (Note.IsDirty)
            {
                Note.SaveCommand.Execute(null);
            }
        }

        private void Content_TextChanged(object sender, TextChangedEventArgs e)
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void OnTop_Click(object sender, RoutedEventArgs e)
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = OnTopToggle.IsChecked == true;
            }

            Persist();
        }
    }
}
