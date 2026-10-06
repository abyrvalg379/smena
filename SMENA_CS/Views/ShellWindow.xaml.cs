using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SMENA.Models;
using SMENA.Services;
using SMENA.ViewModels;

namespace SMENA.Views
{
    public partial class ShellWindow : Window
    {
        private readonly MainViewModel _vm;
        private readonly Dictionary<string, UserControl> _pages;

        public ShellWindow(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;

            _pages = new Dictionary<string, UserControl>
            {
                ["Dashboard"] = new DashboardPage(vm),
                ["Timeline"] = new TimelinePage(vm),
                ["Reports"] = new ReportsPage(vm),
                ["Journal"] = new JournalPage(vm),
                ["Applications"] = new ApplicationsPage(vm),
                ["Settings"] = new SettingsPage(vm),
            };

            Closing += (_, args) =>
            {
                if (!App.IsExiting)
                {
                    args.Cancel = true;
                    Hide();
                }
            };

            _vm.NavigateRequested += NavigateToPage;
            StateChanged += ShellWindow_StateChanged;
            Nav_Click(NavDashboard, new RoutedEventArgs());
        }

        private void NavigateToPage(string key)
        {
            foreach (var child in ((StackPanel)NavDashboard.Parent).Children)
                if (child is Button b)
                    b.Tag = b.Name == "Nav" + key ? "Active" : null;
            PageTitle.Text = TitleFor(key);
            ShowPage(key);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key == Key.F11) ToggleFullscreen();
            if (e.Key == Key.K && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                _vm.SearchOpen = !_vm.SearchOpen;
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape && _vm.SearchOpen)
            {
                _vm.SearchOpen = false;
                e.Handled = true;
            }
        }

        private void SearchBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            }
            else
            {
                _vm.SearchText = "";
            }
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                _vm.Jump(SearchList.SelectedItem as SearchHit ?? _vm.SearchHits.FirstOrDefault());
            }
        }

        private void SearchList_DoubleClick(object sender, MouseButtonEventArgs e) =>
            _vm.Jump(SearchList.SelectedItem as SearchHit);

        private void SearchToggle_Click(object sender, RoutedEventArgs e) =>
            _vm.SearchOpen = !_vm.SearchOpen;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECTW { public int Left, Top, Right, Bottom; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MONITORINFOW
        {
            public int CbSize;
            public RECTW Monitor, Work;
            public int Flags;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOW info);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECTW rect);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOOWNERZORDER = 0x0200;

        /// <summary>Work area (screen minus taskbar) of the monitor the window is on.</summary>
        private RECTW WorkAreaOfMonitor(IntPtr hwnd)
        {
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFOW { CbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOW>() };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
                return info.Work;
            return new RECTW
            {
                Left = (int)SystemParameters.VirtualScreenLeft,
                Top = (int)SystemParameters.VirtualScreenTop,
                Right = (int)(SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth),
                Bottom = (int)(SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
            };
        }

        /// <summary>Borderless windows maximize OVER the taskbar. WindowChrome re-applies
        /// its own full-monitor frame right AFTER StateChanged, so a single pin races and
        /// loses. Pin via physical SetWindowPos, deferred and retried, until it sticks.</summary>
        private void ShellWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState != WindowState.Maximized) return;
            PinMaximizedToWorkArea();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
            int tries = 0;
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (WindowState != WindowState.Maximized) return;
                PinMaximizedToWorkArea();
                if (++tries < 4) timer.Start();
            };
            timer.Start();
        }

        private void PinMaximizedToWorkArea()
        {
            if (WindowState != WindowState.Maximized) return;
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return;
            var wa = WorkAreaOfMonitor(hwnd);
            // already pinned (within 2 px) — WindowChrome has not re-applied, nothing to fight
            if (Math.Abs(r.Left - wa.Left) <= 2 && Math.Abs(r.Top - wa.Top) <= 2 &&
                Math.Abs(r.Right - wa.Right) <= 2 && Math.Abs(r.Bottom - wa.Bottom) <= 2) return;
            SetWindowPos(hwnd, IntPtr.Zero, wa.Left, wa.Top,
                wa.Right - wa.Left, wa.Bottom - wa.Top,
                SWP_NOZORDER | SWP_NOOWNERZORDER);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var src = (System.Windows.Interop.HwndSource)PresentationSource.FromVisual(this);
            Services.Dwm.RoundCorners(src.Handle);
        }

        public void ShowShell()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        public void ShowFromTray() => ShowShell();

        private void ShowPage(string key)
        {
            if (_pages.TryGetValue(key, out var page))
                PageHost.Content = page;
        }

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            foreach (var child in ((StackPanel)NavDashboard.Parent).Children)
                if (child is Button b) b.Tag = ReferenceEquals(b, btn) ? "Active" : null;

            var name = btn.Name; // NavDashboard, NavReportsWeek, NavProjectsAll...
            string key =
                name.StartsWith("NavReports") ? "Reports" :
                name.StartsWith("NavProjects") ? "Reports" :
                name.Replace("Nav", "");
            PageTitle.Text = TitleFor(key);
            ShowPage(key);
        }

        private static string TitleFor(string key) => key switch
        {
            "Settings" => "General / Tracking",
            "Applications" => "Applications",
            "Reports" => "Reports",
            "Projects" => "Projects",
            "Journal" => "Journal — closed tasks",
            _ => key,
        };

        private void Chrome_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { }
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            if (!App.IsExiting) App.ShowWidgetSurface();   // dashboard closed -> widget comes back
        }

        private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

        private void WidgetOpen_Click(object sender, RoutedEventArgs e) => App.ShowWidgetSurface();

        private void ToggleFullscreen()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void PrevDay_Click(object sender, RoutedEventArgs e) => _vm.PrevDay();
        private void NextDay_Click(object sender, RoutedEventArgs e) => _vm.NextDay();
        private void Today_Click(object sender, RoutedEventArgs e) => _vm.SelectedDate = DateTime.Today;
        private void Pause_Click(object sender, RoutedEventArgs e) => _vm.TogglePause();

        private void StartStop_Click(object sender, RoutedEventArgs e) => _vm.StartTrackingQuick();

        private void NewTask_Click(object sender, RoutedEventArgs e)
        {
            ShowMini("NEWTASK", "TASK NAME   ·   ALREADY DONE (HH:MM, empty = don't start)", "", null);
        }

        // ---- mini popup: new task / rename / start-with / keywords ----

        private string _miniMode = "";
        private TaskTimerRow? _miniRow;

        private void ShowMini(string mode, string label, string value, TaskTimerRow? row)
        {
            _miniMode = mode;
            _miniRow = row;
            MiniLabel.Text = label;
            MiniBox1.Text = value;
            MiniBox2.Text = "";
            MiniBox2.Visibility = mode == "NEWTASK" ? Visibility.Visible : Visibility.Collapsed;
            MiniPopup.PlacementTarget = this;
            MiniPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            MiniPopup.IsOpen = true;
            Dispatcher.BeginInvoke(new Action(() => MiniBox1.Focus()),
                System.Windows.Threading.DispatcherPriority.Input);
        }

        private void MiniOk_Click(object sender, RoutedEventArgs e)
        {
            CommitMini();
            MiniPopup.IsOpen = false;
        }

        private void MiniCancel_Click(object sender, RoutedEventArgs e) => MiniPopup.IsOpen = false;

        private void MiniBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { CommitMini(); MiniPopup.IsOpen = false; e.Handled = true; }
            else if (e.Key == Key.Escape) { MiniPopup.IsOpen = false; e.Handled = true; }
        }

        private void CommitMini()
        {
            switch (_miniMode)
            {
                case "NEWTASK":
                    var t = _vm.QuickAddTask(MiniBox1.Text);
                    var off = MiniBox2.Text.Trim();
                    if (t != null && off.Length > 0) _vm.StartManualWithOffset(t.Id, off);
                    break;
                case "RENAME":
                    if (_miniRow?.TaskId is Guid rid) _vm.RenameTask(rid, MiniBox1.Text);
                    break;
                case "STARTWITH":
                    if (_miniRow?.TaskId is Guid sid) _vm.StartManualWithOffset(sid, MiniBox1.Text);
                    break;
                case "KEYWORDS":
                    if (_miniRow?.TaskId is Guid kid) _vm.SetTaskKeywords(kid, MiniBox1.Text);
                    break;
                case "PHASE":
                    if (_miniRow?.TaskId is Guid phid) _vm.SetTaskPhase(phid, MiniBox1.Text);
                    break;
            }
        }

        // ---- quick task context menu ----

        // ---- Quick Actions: press a row, drag over another, release to merge ----
        // Same mouse-capture mechanics as the gantt. Unsorted dropped on a task re-buckets
        // all Unsorted time (nothing deleted); a task dropped on a task merges them and the
        // source task is removed (confirmed first).

        private TaskTimerRow? _quickDragSource;
        private ListBoxItem? _quickHover;

        private void QuickTaskList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _quickHover = null;
            _quickDragSource = ItemsControl.ContainerFromElement(QuickTaskList, e.OriginalSource as DependencyObject) is ListBoxItem li
                ? li.DataContext as TaskTimerRow
                : null;
            if (_quickDragSource != null)
                QuickTaskList.CaptureMouse();
        }

        private void QuickTaskList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_quickDragSource == null || !QuickTaskList.IsMouseCaptured) return;
            var item = ItemsControl.ContainerFromElement(QuickTaskList, e.OriginalSource as DependencyObject) as ListBoxItem;
            var target = item?.DataContext as TaskTimerRow;
            if (target == null || !target.IsTask || ReferenceEquals(target, _quickDragSource))
            {
                ClearQuickHover();
                return;
            }
            if (!ReferenceEquals(_quickHover, item))
            {
                ClearQuickHover();
                _quickHover = item;
                item.Background = ThemeApplier.OptBrush("Hover") ?? System.Windows.Media.Brushes.DimGray;
            }
        }

        private void QuickTaskList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var over = ItemsControl.ContainerFromElement(QuickTaskList, e.OriginalSource as DependencyObject) is ListBoxItem li
                ? li.DataContext as TaskTimerRow
                : null;
            var src = _quickDragSource;
            var hadTarget = _quickHover != null;
            ClearQuickHover();
            if (QuickTaskList.IsMouseCaptured) QuickTaskList.ReleaseMouseCapture();
            _quickDragSource = null;

            if (src == null || !hadTarget || over == null || !over.IsTask || ReferenceEquals(src, over)) return;

            if (src.IsTask)
            {
                var res = System.Windows.MessageBox.Show(
                    "Слить «" + src.Name + "» в «" + over.Name + "»?\n\nВся время перенесётся, задача «" + src.Name + "» будет удалена.",
                    "SMENA — merge", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
                if (res != System.Windows.MessageBoxResult.Yes) return;
                _vm.MergeTasks(src.TaskId!.Value, over.TaskId!.Value);
                _vm.StatusText = "«" + src.Name + "» слита в «" + over.Name + "»";
            }
            else
            {
                _vm.MergeUnsortedInto(over.TaskId!.Value);
                _vm.StatusText = "Всё время Unsorted перенесено в «" + over.Name + "»";
            }
        }

        private void ClearQuickHover()
        {
            if (_quickHover == null) return;
            _quickHover.Background = System.Windows.Media.Brushes.Transparent;
            _quickHover = null;
        }

        private static TaskTimerRow? RowFromMenu(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement fe } } &&
                fe.DataContext is TaskTimerRow r1) return r1;
            return (sender as FrameworkElement)?.DataContext as TaskTimerRow;
        }

        private void QuickMenu_Opened(object sender, RoutedEventArgs e)
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

            AddItem("Start with time…", QMenuStartWith_Click);
            if (row.TaskId == null)
            {
                AddItem("Merge ALL Unsorted into…", QMenuMerge_Click);
                AddItem("Reset to zero…", QMenuResetUnsorted_Click);
            }
            else
            {
                AddItem("Rename…", QMenuRename_Click);
                AddItem("Keywords…", QMenuKeywords_Click);
                AddItem("Phase…", QMenuPhase_Click);
                AddColorMenu(menu, row);
                AddItem("Merge into…", QMenuMerge_Click);
                AddItem(row.Archived ? "Reopen" : "Close task", QMenuArchive_Click);
                AddItem(row.Archived ? "Reopen project" : "Close project", QMenuArchiveProject_Click);
                AddItem("Delete", QMenuDelete_Click);
            }
        }

        /// <summary>Color submenu: accent color in stats; "Auto" returns to the palette hash.</summary>
        private void AddColorMenu(ContextMenu menu, TaskTimerRow row)
        {
            var currentHex = row.TaskId is Guid cid ? _vm.GetTask(cid)?.ColorHex ?? "" : "";

            var colorRoot = new MenuItem { Header = "Color" };

            var auto = new MenuItem { Header = "Auto (palette)", IsChecked = currentHex.Length == 0 };
            auto.Click += (_, _) => { if (row.TaskId is Guid aid) _vm.SetTaskColor(aid, null); };
            colorRoot.Items.Add(auto);

            foreach (var hex in Palette.Colors)
            {
                var mi = new MenuItem
                {
                    Header = hex,
                    IsChecked = string.Equals(currentHex, hex, StringComparison.OrdinalIgnoreCase),
                    Icon = new System.Windows.Controls.Border
                    {
                        Width = 12, Height = 12, CornerRadius = new CornerRadius(3),
                        Background = Palette.Frozen(hex)
                    }
                };
                var captured = hex;
                mi.Click += (_, _) => { if (row.TaskId is Guid sid) _vm.SetTaskColor(sid, captured); };
                colorRoot.Items.Add(mi);
            }
            menu.Items.Add(colorRoot);
        }

        private void QMenuStartWith_Click(object sender, RoutedEventArgs e)
        {
            var row = RowFromMenu(sender, e);
            if (row != null) ShowMini("STARTWITH", "ALREADY DONE (HH:MM) — timer continues from it", "", row);
        }

        private void QMenuRename_Click(object sender, RoutedEventArgs e)
        {
            var row = RowFromMenu(sender, e);
            if (row != null) ShowMini("RENAME", "TASK NAME", row.Name, row);
        }

        private void QMenuKeywords_Click(object sender, RoutedEventArgs e)
        {
            var row = RowFromMenu(sender, e);
            if (row?.TaskId is Guid kid) ShowMini("KEYWORDS", "KEYWORDS (comma-separated)", _vm.GetTask(kid)?.Keywords ?? "", row);
        }

        private void QMenuMerge_Click(object sender, RoutedEventArgs e)
        {
            var row = RowFromMenu(sender, e);
            if (row == null) return;
            if (row.TaskId == null) _vm.BeginMergeUnsorted();
            else _vm.BeginMerge(row.TaskId.Value);
        }

        private void QMenuResetUnsorted_Click(object sender, RoutedEventArgs e) => _vm.ResetUnsortedToZero();

        private void QMenuPhase_Click(object sender, RoutedEventArgs e)
        {
            var row = RowFromMenu(sender, e);
            if (row?.TaskId is Guid pid) ShowMini("PHASE", "PHASE — e.g. modeling, texturing, damage", _vm.GetTask(pid)?.Phase ?? "", row);
        }

        private void QMenuArchive_Click(object sender, RoutedEventArgs e)
        {
            var row = RowFromMenu(sender, e);
            if (row?.TaskId is Guid id)
            {
                var t = _vm.GetTask(id);
                if (t != null) _vm.SetTaskArchived(id, t.ArchivedAt == null);
            }
        }

        private void QMenuArchiveProject_Click(object sender, RoutedEventArgs e)
        {
            var row = RowFromMenu(sender, e);
            if (row?.TaskId is Guid tid)
            {
                var t = _vm.GetTask(tid);
                if (t != null) _vm.SetProjectArchived(t.ProjectId, !_vm.IsProjectArchived(t.ProjectId));
            }
        }

        private void QMenuDelete_Click(object sender, RoutedEventArgs e)
        {
            var row = RowFromMenu(sender, e);
            if (row?.TaskId is Guid did)
            {
                var name = _vm.GetTask(did)?.Name;
                if (MessageBox.Show($"Delete task '{name}'? Its tracked time goes to Unsorted.",
                        "SMENA", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                    _vm.DeleteTaskNow(did);
            }
        }

        private void CloseTaskQuick_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is TaskTimerRow { TaskId: Guid id }) _vm.CloseTask(id);
        }

        private void ManualEntry_Click(object sender, RoutedEventArgs e)
        {
            _vm.AddManualBlock();
            Nav_Click(NavTimeline, new RoutedEventArgs());
        }

        private void CloseDetails_Click(object sender, RoutedEventArgs e) => _vm.SelectedSession = null;
        private void SessionSaveNote_Click(object sender, RoutedEventArgs e) => _vm.SaveSessionNote();
        private void SessionEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedSession != null) _vm.BeginEditBlock(_vm.SelectedSession);
        }
        private void SessionSplit_Click(object sender, RoutedEventArgs e) => _vm.SplitSession();
        private void SessionAssign_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedSession != null) _vm.BeginAssign(_vm.SelectedSession);
        }
        private void SessionDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedSession == null) return;
            _vm.BeginEditBlock(_vm.SelectedSession);
            _vm.DeleteEditingBlocks();
        }

        private void UpdateDismiss_Click(object sender, RoutedEventArgs e) => _vm.DismissUpdate();
        private void UpdateDownload_Click(object sender, RoutedEventArgs e) => _vm.OpenReleasesPage();

        private void AssignClose_Click(object sender, RoutedEventArgs e) => _vm.CancelAssign();
        private void EditBlockClose_Click(object sender, RoutedEventArgs e) => _vm.CloseEditBlock();
        private void EditBlockSave_Click(object sender, RoutedEventArgs e) => _vm.SaveEditBlock();
        private void EditBlockDelete_Click(object sender, RoutedEventArgs e) => _vm.DeleteEditingBlocks();
        private void EditBlockUnsort_Click(object sender, RoutedEventArgs e) => _vm.EditBlockTask = null;

        private void AssignSearchBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue && sender is TextBox tb)
                tb.Dispatcher.BeginInvoke(() => tb.Focus());
        }

        private void AssignList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is TrackedTask task)
            {
                _vm.AssignTo(task);
                if (sender is ListBox lb) lb.SelectedItem = null;
            }
        }
    }
}
