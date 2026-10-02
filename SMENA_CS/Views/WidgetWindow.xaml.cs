using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SMENA.Models;
using SMENA.Services;
using SMENA.ViewModels;

namespace SMENA.Views
{
    /// <summary>Compact always-available widget: live clock, task rows, quick add, mini timeline.</summary>
    public partial class WidgetWindow : Window
    {
        // global hotkeys: Ctrl+Alt+S start/stop · Ctrl+Alt+P pause · Ctrl+Alt+U show/hide
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x1;
        private const uint MOD_CONTROL = 0x2;

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private IntPtr _hwnd;
        private bool _hotkeysRegistered;

        private readonly MainViewModel _vm;
        private bool _positionApplied;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var src = (HwndSource)PresentationSource.FromVisual(this);
            _hwnd = src.Handle;
            src.AddHook(WndProc);
            // isolated (demo) instances never own global hotkeys: two instances registering
            // Ctrl+Alt+S/P/U means the hotkeys can end up driving the demo instead of the app.
            if (App.Isolated) return;
            RegisterHotKey(_hwnd, 1, MOD_CONTROL | MOD_ALT, 0x53); // Ctrl+Alt+S
            RegisterHotKey(_hwnd, 2, MOD_CONTROL | MOD_ALT, 0x50); // Ctrl+Alt+P
            RegisterHotKey(_hwnd, 3, MOD_CONTROL | MOD_ALT, 0x55); // Ctrl+Alt+U
            _hotkeysRegistered = true;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_HOTKEY) return IntPtr.Zero;
            handled = true;
            switch (wParam.ToInt32())
            {
                case 1: _vm.StartTrackingQuick(); break;
                case 2: _vm.TogglePause(); break;
                case 3:
                    App.ToggleSurface();
                    break;
            }
            return IntPtr.Zero;
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_hotkeysRegistered && _hwnd != IntPtr.Zero)
            {
                UnregisterHotKey(_hwnd, 1);
                UnregisterHotKey(_hwnd, 2);
                UnregisterHotKey(_hwnd, 3);
                _hotkeysRegistered = false;
            }
            base.OnClosed(e);
        }

        public WidgetWindow(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;

            Closing += (_, args) =>
            {
                if (!App.IsExiting)
                {
                    args.Cancel = true;
                    Hide();
                }
            };
            PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Hide(); };

            _vm.DataChanged += RenderMini;
            _vm.SecondTick += RenderMini;
            MiniTimeline.SizeChanged += (_, _) => RenderMini();            Loaded += (_, _) => FadeIn();
        }

        public void ShowWidget()
        {
            ApplyPosition();
            Show();
            Activate();
            RenderMini();
            FadeIn();
        }

        public void ShowFromTray() => ShowWidget();

        private void ApplyPosition()
        {
            if (_positionApplied) return;
            _positionApplied = true;

            // multi-monitor aware: validate against the WHOLE virtual desktop, not the
            // primary work area — a widget parked on a secondary screen must survive restarts
            var vsL = SystemParameters.VirtualScreenLeft;
            var vsT = SystemParameters.VirtualScreenTop;
            var vsR = vsL + SystemParameters.VirtualScreenWidth;
            var vsB = vsT + SystemParameters.VirtualScreenHeight;

            if (_vm.WidgetLeft is double l && _vm.WidgetTop is double t)
            {
                // clamp (don't reset): a resolution change keeps the widget reachable
                Left = Math.Clamp(l, vsL, Math.Max(vsL, vsR - Width));
                Top = Math.Clamp(t, vsT, Math.Max(vsT, vsB - 450));
            }
            else
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Right - Width - 20;
                Top = wa.Top + 70;
            }
        }

        private void FadeIn()
        {
            var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase() };
            BeginAnimation(OpacityProperty, anim);
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            try { DragMove(); } catch { /* inactive window race */ }
            _vm.SaveWidgetPos(Left, Top);
        }

        private void Pin_Click(object sender, RoutedEventArgs e) => _vm.WidgetTopmost = !_vm.WidgetTopmost;
        private void Pause_Click(object sender, RoutedEventArgs e) => _vm.TogglePause();
        private void Dashboard_Click(object sender, RoutedEventArgs e) => App.ShowDashboardSurface();
        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Hide();

        private void RowToggle_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is TaskTimerRow row)
                _vm.ToggleManualFor(row.TaskId);
        }

        // ---- row context menu ----

        /// <summary>Built dynamically: Unsorted gets reset-to-zero, tasks get keywords/rename.</summary>
        private void RowMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu menu) return;
            menu.Items.Clear();
            var row = (menu.PlacementTarget as FrameworkElement)?.DataContext as TaskTimerRow;
            if (row == null) return;

            void AddItem(string header, RoutedEventHandler handler)
            {
                var mi = new MenuItem { Header = header };
                mi.Click += handler;
                menu.Items.Add(mi);
            }

            AddItem("Start / stop stopwatch", RowMenuToggle_Click);
            AddItem("Start with time…", OffsetMenu_Click);
            if (row.TaskId == null)
                AddItem("Reset to zero…", (_, _) => _vm.ResetUnsortedToZero());
            else
            {
                AddItem("Keywords…", KwMenu_Click);
                AddItem("Rename", RowMenuRename_Click);
            }
        }

        private static TaskTimerRow? RowFromMenu(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement fe } } &&
                fe.DataContext is TaskTimerRow r1) return r1;
            return (sender as FrameworkElement)?.DataContext as TaskTimerRow;
        }

        private void RowMenuToggle_Click(object sender, RoutedEventArgs e)
        {
            var row = RowFromMenu(sender, e);
            if (row != null) _vm.ToggleManualFor(row.TaskId);
        }

        private void RowMenuRename_Click(object sender, RoutedEventArgs e)
        {
            var row = RowFromMenu(sender, e);
            if (row != null) _vm.BeginRename(row);
        }

        private TaskTimerRow? _kwRow;

        private void KwMenu_Click(object sender, RoutedEventArgs e)
        {
            var row = RowFromMenu(sender, e);
            if (row?.TaskId is not Guid id) return;
            _kwRow = row;
            KwBox.Text = _vm.GetTask(id)?.Keywords ?? "";
            // Anchor to the window, not the menu item: the item is destroyed when the
            // menu closes, which used to pull the popup down with it.
            KwPopup.PlacementTarget = this;
            KwPopup.Placement = PlacementMode.MousePoint;
            KwPopup.IsOpen = true;
            Dispatcher.BeginInvoke(new Action(() => KwBox.Focus()),
                System.Windows.Threading.DispatcherPriority.Input);
        }

        private void KwBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (_kwRow?.TaskId is Guid id) _vm.SetTaskKeywords(id, KwBox.Text);
                KwPopup.IsOpen = false;
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                KwPopup.IsOpen = false;
                e.Handled = true;
            }
        }

        private void WidgetNameEdit_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox { DataContext: TaskTimerRow row }) return;
            if (e.Key == Key.Enter) { _vm.CommitRename(row); e.Handled = true; }
            else if (e.Key == Key.Escape) { row.IsEditing = false; e.Handled = true; }
        }

        private void WidgetNameEdit_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue && sender is TextBox tb)
                tb.Dispatcher.BeginInvoke(() => { tb.Focus(); tb.SelectAll(); });
        }

        // ---- start with offset ----

        private TaskTimerRow? _offsetRow;

        private void OffsetMenu_Click(object sender, RoutedEventArgs e)
        {
            var row = RowFromMenu(sender, e);
            if (row == null) return;
            _offsetRow = row;
            OffsetBox.Text = "";
            OffsetPopup.PlacementTarget = this;
            OffsetPopup.Placement = PlacementMode.MousePoint;
            OffsetPopup.IsOpen = true;
            Dispatcher.BeginInvoke(new Action(() => { OffsetBox.Focus(); OffsetBox.SelectAll(); }),
                System.Windows.Threading.DispatcherPriority.Input);
        }

        private void OffsetBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (_offsetRow?.TaskId is Guid id)
                    _vm.StartManualWithOffset(id, OffsetBox.Text);
                OffsetPopup.IsOpen = false;
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                OffsetPopup.IsOpen = false;
                e.Handled = true;
            }
        }

        private void QuickBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || sender is not TextBox tb) return;
            _vm.QuickAddTask(tb.Text);
            tb.Clear();
            e.Handled = true;
        }

        private void QuickAdd_Click(object sender, RoutedEventArgs e)
        {
            _vm.QuickAddTask(QuickBox.Text);
            QuickBox.Clear();
            QuickBox.Focus();
        }

        private void RenderMini()
        {
            if (MiniTimeline.ActualWidth < 10) return;
            var canvas = MiniTimeline;
            canvas.Children.Clear();

            var blocks = _vm.TodayBlocksForTimeline();
            var now = DateTime.Now;
            var (start, end) = Services.Timeline.Window(blocks, now);
            double w = canvas.ActualWidth;
            double spanMin = (end - start).TotalMinutes;

            foreach (var b in blocks)
            {
                double x = (b.Start - start).TotalMinutes / spanMin * w;
                double bw = Math.Max(2.0, (b.End - b.Start).TotalMinutes / spanMin * w);
                var rect = new Rectangle
                {
                    Width = bw,        // Canvas gives no size: without Width a Rectangle renders zero-wide
                    Height = 14,
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = BrushFor(b.TaskId),
                    Cursor = Cursors.Hand,
                    ToolTip = $"{b.Start:HH:mm}–{b.End:HH:mm} · {_vm.TaskNameFor(b.TaskId)}"
                };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, 3);
                canvas.Children.Add(rect);
            }

            double nx = (now - start).TotalMinutes / spanMin * w;
            if (nx >= 0 && nx <= w)
            {
                var line = new Rectangle { Width = 1.5, Height = 18, Fill = ThemeBrush("Accent", "#5AC8FA") };
                Canvas.SetLeft(line, nx);
                Canvas.SetTop(line, 1);
                canvas.Children.Add(line);
            }

            // hour labels — makes the strip readable
            var labels = MiniTimelineLabels;
            labels.Children.Clear();
            if (labels.ActualWidth < 10) return;
            double stepHours = (end - start).TotalHours <= 8 ? 1 : 2;
            var t = new DateTime(start.Year, start.Month, start.Day, start.Hour, 0, 0).AddHours(1);
            while (t < end)
            {
                double lx = (t - start).TotalMinutes / spanMin * w;
                var tb = new TextBlock
                {
                    Text = t.ToString("HH"),
                    FontSize = 9,
                    Foreground = ThemeBrush("FgDim", "#6E6E78")
                };
                Canvas.SetLeft(tb, lx + 3);
                Canvas.SetTop(tb, 0);
                labels.Children.Add(tb);
                t = t.AddHours(stepHours);
            }
        }

        // task accent color: custom ColorHex wins, else palette hash — same as everywhere else
        private Brush BrushFor(Guid? taskId)
        {
            if (taskId == null)
                return FrozenBrush("#5A5A64");
            var t = _vm.GetTask(taskId.Value);
            return Palette.BrushFor(taskId, t?.ColorHex);
        }

        private static Brush ThemeBrush(string key, string fallback) =>
            ThemeApplier.OptBrush(key) ?? FrozenBrush(fallback);

        private static SolidColorBrush FrozenBrush(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }
    }
}
