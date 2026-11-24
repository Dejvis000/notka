using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Notka.Desktop.ViewModels;
using Windows.Graphics;
using Windows.System;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Notka.Desktop
{
    public sealed partial class MainWindow : Window
    {
        private const int DefaultWidth = 1180;
        private const int DefaultHeight = 760;
        private const int MinWidth = 900;
        private const int MinHeight = 520;

        public MainWindow(MainViewModel viewModel, SettingsViewModel settings)
        {
            ViewModel = viewModel;
            Settings = settings;
            InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(TitleBarArea);
            SystemBackdrop = new MicaBackdrop();
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            ApplySize();
            AppWindow.Closing += OnClosing;
            ViewModel.Tasks.PropertyChanged += OnTasksPropertyChanged;
        }

        public MainViewModel ViewModel { get; }

        public SettingsViewModel Settings { get; }

        public bool AllowClose { get; set; }

        private void ApplySize()
        {
            var scale = PInvoke.GetDpiForWindow(new HWND(WinRT.Interop.WindowNative.GetWindowHandle(this))) / 96.0;
            var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            var width = Math.Min((int)(DefaultWidth * scale), workArea.Width);
            var height = Math.Min((int)(DefaultHeight * scale), workArea.Height);
            AppWindow.MoveAndResize(new RectInt32(
                workArea.X + ((workArea.Width - width) / 2),
                workArea.Y + ((workArea.Height - height) / 2),
                width,
                height));
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = (int)(MinWidth * scale);
                presenter.PreferredMinimumHeight = (int)(MinHeight * scale);
            }
        }

        private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (!AllowClose)
            {
                args.Cancel = true;
                sender.Hide();
                _ = ViewModel.Notes.SaveDirtyAsync();
            }
        }

        private void OnTasksPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(TasksViewModel.SelectedTab))
            {
                return;
            }

            var tag = ViewModel.Tasks.SelectedTab.ToString();
            var item = TabBar.Items.FirstOrDefault(i => i.Tag as string == tag);
            if (item is not null && TabBar.SelectedItem != item)
            {
                TabBar.SelectedItem = item;
            }
        }

        private void Escape_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            if (Settings.IsOpen)
            {
                Settings.CloseCommand.Execute(null);
                args.Handled = true;
            }
        }

        private void TabBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        {
            if (sender.SelectedItem?.Tag is string tag && Enum.TryParse<TaskTab>(tag, out var tab))
            {
                ViewModel.Tasks.SelectedTab = tab;
            }
        }

        private void DuePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) =>
            ViewModel.Tasks.NewDueDate = args.NewDate?.DateTime;

        private async void AddTask_Click(object sender, RoutedEventArgs e) => await AddTaskAsync();

        private async void NewTaskBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                await AddTaskAsync();
            }
        }

        private async void NewNoteBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                await ViewModel.Notes.AddAsync();
            }
        }

        private void Note_LostFocus(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is NoteViewModel { IsDirty: true } note)
            {
                note.SaveCommand.Execute(null);
            }
        }

        private async Task AddTaskAsync()
        {
            await ViewModel.Tasks.AddAsync();
            if (!ViewModel.Tasks.NewDueDate.HasValue)
            {
                DuePicker.Date = null;
            }
        }
    }
}
