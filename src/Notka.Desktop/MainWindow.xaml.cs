using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Notka.Desktop.Updates;
using Notka.Desktop.ViewModels;
using Windows.Graphics;
using Windows.System;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Notka.Desktop
{
    public sealed partial class MainWindow : Window
    {
        private const int NormalMinWidth = 900;
        private const int NormalMinHeight = 520;
        private const int CompactMinWidth = 340;
        private const int CompactMinHeight = 300;

        public MainWindow(MainViewModel viewModel, SettingsViewModel settings, WindowModeViewModel windowMode, UpdateViewModel updates)
        {
            ViewModel = viewModel;
            Settings = settings;
            WindowMode = windowMode;
            Updates = updates;
            InitializeComponent();
            FillDueTimeLists();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(TitleBarArea);
            SystemBackdrop = new MicaBackdrop();
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            ApplyMode(center: true);
            AppWindow.Closing += OnClosing;
            ViewModel.Tasks.PropertyChanged += OnTasksPropertyChanged;
            WindowMode.PropertyChanged += OnWindowModeChanged;
        }

        public MainViewModel ViewModel { get; }

        public SettingsViewModel Settings { get; }

        public WindowModeViewModel WindowMode { get; }

        public UpdateViewModel Updates { get; }

        public bool AllowClose { get; set; }

        private double Scale => PInvoke.GetDpiForWindow(new HWND(WinRT.Interop.WindowNative.GetWindowHandle(this))) / 96.0;

        private void ApplyMode(bool center)
        {
            var scale = Scale;
            var compact = WindowMode.IsCompact;
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = (int)((compact ? CompactMinWidth : NormalMinWidth) * scale);
                presenter.PreferredMinimumHeight = (int)((compact ? CompactMinHeight : NormalMinHeight) * scale);
                presenter.IsAlwaysOnTop = WindowMode.KeepOnTop;
            }

            var (dipWidth, dipHeight) = WindowMode.GetSize(compact);
            var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            var width = Math.Min((int)(dipWidth * scale), workArea.Width);
            var height = Math.Min((int)(dipHeight * scale), workArea.Height);
            if (center)
            {
                AppWindow.MoveAndResize(new RectInt32(
                    workArea.X + ((workArea.Width - width) / 2),
                    workArea.Y + ((workArea.Height - height) / 2),
                    width,
                    height));
            }
            else
            {
                AppWindow.Resize(new SizeInt32(width, height));
            }
        }

        private void OnWindowModeChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(WindowModeViewModel.IsCompact))
            {
                if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized } maximized)
                {
                    maximized.Restore();
                }

                StoreCurrentSize(!WindowMode.IsCompact);
                ApplyMode(center: false);
            }
            else if (e.PropertyName == nameof(WindowModeViewModel.KeepOnTop) && AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = WindowMode.KeepOnTop;
            }
        }

        public void StoreCurrentSize() => StoreCurrentSize(WindowMode.IsCompact);

        private void StoreCurrentSize(bool compact)
        {
            if (AppWindow.Presenter is OverlappedPresenter { State: not OverlappedPresenterState.Restored })
            {
                return;
            }

            var scale = Scale;
            WindowMode.SetSize(compact, (int)Math.Round(AppWindow.Size.Width / scale), (int)Math.Round(AppWindow.Size.Height / scale));
        }

        private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            StoreCurrentSize();
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

            var index = (int)ViewModel.Tasks.SelectedTab;
            if (CompactTabBox.SelectedIndex != index)
            {
                CompactTabBox.SelectedIndex = index;
            }
        }

        private void CompactSection_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        {
            if (sender.SelectedItem?.Tag is string tag)
            {
                WindowMode.ShowTasks = tag == "Tasks";
            }
        }

        private void CompactTab_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var index = ((ComboBox)sender).SelectedIndex;
            if (index >= 0)
            {
                ViewModel.Tasks.SelectedTab = (TaskTab)index;
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

        private bool? _settingsWide;

        private void SettingsPage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var wide = e.NewSize.Width >= 760;
            if (_settingsWide == wide)
            {
                return;
            }

            _settingsWide = wide;
            var target = wide ? SettingsRight : SettingsLeft;
            foreach (var card in new[] { CardUpdates, CardAgents })
            {
                (card.Parent as Panel)?.Children.Remove(card);
                target.Children.Add(card);
            }

            SettingsRight.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
            SettingsGrid.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            SettingsGrid.ColumnSpacing = wide ? 16 : 0;
        }

        private void TabBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        {
            if (sender.SelectedItem?.Tag is string tag && Enum.TryParse<TaskTab>(tag, out var tab))
            {
                ViewModel.Tasks.SelectedTab = tab;
            }
        }

        private bool _syncingDue;

        private void DueCalendar_SelectedDatesChanged(CalendarView sender, CalendarViewSelectedDatesChangedEventArgs args)
        {
            if (!_syncingDue)
            {
                ViewModel.Tasks.NewDueDate = sender.SelectedDates.Count > 0 ? sender.SelectedDates[0].DateTime.Date : null;
                UpdateDueText();
            }
        }

        private void FillDueTimeLists()
        {
            for (var hour = 0; hour < 24; hour++)
            {
                DueHourList.Items.Add(hour.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
            }

            for (var minute = 0; minute < 60; minute += 5)
            {
                DueMinuteList.Items.Add(minute.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        private void DueTimeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingDue)
            {
                return;
            }

            if (DueHourList.SelectedIndex < 0)
            {
                if (DueMinuteList.SelectedIndex < 0)
                {
                    SelectDueTime(null);
                }

                return;
            }

            _syncingDue = true;
            var minutes = DueMinuteList.SelectedIndex >= 0
                ? DueMinuteList.SelectedIndex * 5
                : ViewModel.Tasks.NewDueTime?.Minutes ?? 0;
            if (DueMinuteList.SelectedIndex < 0 && minutes % 5 == 0)
            {
                DueMinuteList.SelectedIndex = minutes / 5;
            }

            var time = new TimeSpan(DueHourList.SelectedIndex, minutes, 0);
            DueTimeBox.Text = $"{time:hh\\:mm}";
            _syncingDue = false;
            ViewModel.Tasks.NewDueTime = time;
            UpdateDueText();
        }

        private void DueTimeBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncingDue)
            {
                return;
            }

            var text = DueTimeBox.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                SelectDueTime(null);
                return;
            }

            if (DueTimeInput.TryParse(text, out var time))
            {
                SelectDueTime(time);
            }
        }

        private void SelectDueTime(TimeSpan? time)
        {
            _syncingDue = true;
            DueHourList.SelectedIndex = time is { } t ? t.Hours : -1;
            DueMinuteList.SelectedIndex = time is { } m && m.Minutes % 5 == 0 ? m.Minutes / 5 : -1;
            if (DueHourList.SelectedItem is { } hourItem)
            {
                DueHourList.ScrollIntoView(hourItem);
            }

            if (DueMinuteList.SelectedItem is { } minuteItem)
            {
                DueMinuteList.ScrollIntoView(minuteItem);
            }

            _syncingDue = false;
            ViewModel.Tasks.NewDueTime = time;
            UpdateDueText();
        }

        private void ClearDue_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.Tasks.NewDueDate = null;
            ViewModel.Tasks.NewDueTime = null;
            ResetDueControls();
        }

        private void ResetDueControls()
        {
            _syncingDue = true;
            DueCalendar.SelectedDates.Clear();
            DueHourList.SelectedIndex = -1;
            DueMinuteList.SelectedIndex = -1;
            DueTimeBox.Text = string.Empty;
            _syncingDue = false;
            UpdateDueText();
        }

        private void UpdateDueText()
        {
            var date = ViewModel.Tasks.NewDueDate;
            var time = ViewModel.Tasks.NewDueTime;
            if (date is null && time is null)
            {
                DueLabel.Text = "Termin";
                return;
            }

            var day = (date ?? DateTime.Today).Date;
            var dayText = day == DateTime.Today ? "dziś" : day == DateTime.Today.AddDays(1) ? "jutro" : day.ToString("d MMM");
            DueLabel.Text = time is { } t ? $"{dayText} {(t == TimeSpan.Zero ? TimeSpan.FromMinutes(1) : t):hh\\:mm}" : dayText;
        }

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
            if (!ViewModel.Tasks.NewDueDate.HasValue && !ViewModel.Tasks.NewDueTime.HasValue)
            {
                ResetDueControls();
            }
        }
    }
}
