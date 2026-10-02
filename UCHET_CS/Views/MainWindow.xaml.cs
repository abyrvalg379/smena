using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using UCHET.Models;
using UCHET.ViewModels;

namespace UCHET.Views
{
    public partial class MainWindow : Window
    {
        private static readonly string[] Palette =
        {
            "#5AC8FA", "#4CD964", "#FF9F0A", "#FF6482", "#BF5AF2", "#FFD60A", "#64D2FF", "#30D158"
        };

        private readonly MainViewModel _vm;

        public MainWindow(MainViewModel vm)
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

            PreviewKeyDown += Window_PreviewKeyDown;
            _vm.DataChanged += RenderTimeline;
            _vm.SecondTick += RenderTimeline;
            TimelineCanvas.SizeChanged += (_, _) => RenderTimeline();
        }

        public void ShowFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        // ---- global keys: Esc layers (drawer -> inline edit), F2 rename ----

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (_vm.AssignOpen) { _vm.CancelAssign(); e.Handled = true; return; }
                if (_vm.EditBlockOpen) { _vm.CloseEditBlock(); e.Handled = true; return; }
                var editing = _vm.TimerRows.FirstOrDefault(r => r.IsEditing);
                if (editing != null) { editing.IsEditing = false; e.Handled = true; }
            }
            else if (e.Key == Key.F2)
            {
                if (TimerList.SelectedItem is TaskTimerRow row && !row.IsEditing)
                {
                    _vm.BeginRename(row);
                    e.Handled = true;
                }
            }
        }

        // ---- day timeline strip ----

        private void RenderTimeline()
        {
            if (TimelineCanvas.ActualWidth < 20) return;
            var canvas = TimelineCanvas;
            canvas.Children.Clear();
            TimelineLabels.Children.Clear();

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
                    Height = 26,
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = BrushFor(b.TaskId),
                    Cursor = Cursors.Hand,
                    Tag = b
                };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, 2);
                rect.ToolTip = $"{b.Start:HH:mm}–{b.End:HH:mm} · {_vm.TaskNameFor(b.TaskId)}\n{b.Process}: {b.Title}";
                rect.PreviewMouseLeftButtonDown += Segment_Click;
                canvas.Children.Add(rect);
            }

            double nx = (now - start).TotalMinutes / spanMin * w;
            if (nx >= 0 && nx <= w)
            {
                var line = new Rectangle { Width = 1.5, Height = 32, Fill = (Brush)FindResource("BrushAccent") };
                Canvas.SetLeft(line, nx);
                Canvas.SetTop(line, 0);
                canvas.Children.Add(line);
            }

            double stepHours = (end - start).TotalHours <= 10 ? 1 : 2;
            var t = new DateTime(start.Year, start.Month, start.Day, start.Hour, 0, 0).AddHours(1);
            while (t < end)
            {
                double x = (t - start).TotalMinutes / spanMin * w;
                var tb = new TextBlock { Text = t.ToString("HH:mm"), FontSize = 10, Foreground = (Brush)FindResource("BrushDim") };
                Canvas.SetLeft(tb, x + 2);
                TimelineLabels.Children.Add(tb);
                t = t.AddHours(stepHours);
            }
        }

        private Brush BrushFor(Guid? taskId)
        {
            if (taskId == null)
                return FrozenBrush("#55555E");
            int idx = Math.Abs(taskId.Value.GetHashCode()) % Palette.Length;
            return FrozenBrush(Palette[idx]);
        }

        private static SolidColorBrush FrozenBrush(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        private void Segment_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not ActivityBlock b) return;
            ActivityExpander.IsExpanded = true;
            var row = _vm.FindRowForBlock(b);
            if (row != null)
            {
                ActivityLogList.SelectedItem = row;
                ActivityLogList.ScrollIntoView(row);
            }
        }

        // ---- stopwatch board interactions ----

        private void BoardRow_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is Grid { DataContext: TaskTimerRow row } && !row.IsEditing)
            {
                _vm.ToggleManualFor(row.TaskId);
                e.Handled = true;
            }
        }

        private static TaskTimerRow? RowFromEvent(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement fe } } && fe.DataContext is TaskTimerRow r1) return r1;
            return (sender as FrameworkElement)?.DataContext as TaskTimerRow;
        }

        private static BlockRow? BlockFromEvent(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement fe } } && fe.DataContext is BlockRow r1) return r1;
            return (sender as FrameworkElement)?.DataContext as BlockRow;
        }

        private void MenuRowToggle_Click(object sender, RoutedEventArgs e)
        {
            var row = RowFromEvent(sender, e);
            if (row != null) _vm.ToggleManualFor(row.TaskId);
        }

        private void MenuRowRename_Click(object sender, RoutedEventArgs e)
        {
            var row = RowFromEvent(sender, e);
            if (row != null) _vm.BeginRename(row);
        }

        private void NameEdit_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox { DataContext: TaskTimerRow row }) return;
            if (e.Key == Key.Enter) { _vm.CommitRename(row); e.Handled = true; }
            else if (e.Key == Key.Escape) { row.IsEditing = false; e.Handled = true; }
        }

        private void NameEdit_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue && sender is TextBox tb)
                tb.Dispatcher.BeginInvoke(() => { tb.Focus(); tb.SelectAll(); });
        }

        // ---- activity log / assign drawer ----

        private void ActivityLogList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ActivityLogList.SelectedItems.Count > 1)
                _vm.BeginAssignBulk(ActivityLogList.SelectedItems);
            else if (e.AddedItems.Count > 0 && e.AddedItems[0] is BlockRow row)
                _vm.BeginAssign(row);
        }

        private void MenuBlockAssign_Click(object sender, RoutedEventArgs e)
        {
            if (ActivityLogList.SelectedItems.Count > 1)
            {
                _vm.BeginAssignBulk(ActivityLogList.SelectedItems);
                return;
            }
            var row = BlockFromEvent(sender, e);
            if (row != null) _vm.BeginAssign(row);
        }

        private void MenuBlockEdit_Click(object sender, RoutedEventArgs e)
        {
            var row = BlockFromEvent(sender, e);
            if (row != null)
            {
                ActivityExpander.IsExpanded = true;
                _vm.BeginEditBlock(row);
            }
        }

        private void MenuBlockDelete_Click(object sender, RoutedEventArgs e)
        {
            var row = BlockFromEvent(sender, e);
            if (row != null)
            {
                _vm.BeginEditBlock(row);
                _vm.DeleteEditingBlocks();
            }
        }

        private void AddBlock_Click(object sender, RoutedEventArgs e)
        {
            ActivityExpander.IsExpanded = true;
            _vm.AddManualBlock();
        }

        private void Collapse_Click(object sender, RoutedEventArgs e)
        {
            if (ActivityLogList.SelectedItems.Count > 0)
                _vm.CollapseSelected(ActivityLogList.SelectedItems);
        }

        private void MenuBlockCollapse_Click(object sender, RoutedEventArgs e)
        {
            if (ActivityLogList.SelectedItems.Count > 1)
                _vm.CollapseSelected(ActivityLogList.SelectedItems);
            else
            {
                var row = BlockFromEvent(sender, e);
                if (row != null) _vm.CollapseSelected(new[] { row });
            }
        }

        private void EditBlockSave_Click(object sender, RoutedEventArgs e) => _vm.SaveEditBlock();
        private void EditBlockDelete_Click(object sender, RoutedEventArgs e) => _vm.DeleteEditingBlocks();
        private void EditBlockClose_Click(object sender, RoutedEventArgs e) => _vm.CloseEditBlock();
        private void EditBlockUnsort_Click(object sender, RoutedEventArgs e) => _vm.EditBlockTask = null;

        private void MenuBlockCopy_Click(object sender, RoutedEventArgs e)
        {
            var row = BlockFromEvent(sender, e);
            if (row != null) Clipboard.SetText(row.Title ?? "");
        }

        private void AssignClose_Click(object sender, RoutedEventArgs e) => _vm.CancelAssign();

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
                AssignList.SelectedItem = null;
            }
        }

        // ---- header / tasks / settings (v0.1) ----

        private void ManualButton_Click(object sender, RoutedEventArgs e) => _vm.ToggleManual();

        private void AddProject_Click(object sender, RoutedEventArgs e) => _vm.AddProject();
        private void DelProject_Click(object sender, RoutedEventArgs e)
        {
            _vm.DeleteProject(_vm.SelectedProject);
        }

        private void AddTask_Click(object sender, RoutedEventArgs e) => _vm.AddTask();

        private void TasksMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu menu) return;
            menu.Items.Clear();
            if (TasksList.SelectedItem is not TrackedTask task) return;
            foreach (var p in _vm.Projects.Where(p => p.Id != task.ProjectId))
            {
                var projectId = p.Id;
                menu.Items.Add(new MenuItem { Header = $"Move to \u201C{p.Name}\u201D", Tag = projectId });
            }
            foreach (MenuItem item in menu.Items.OfType<MenuItem>())
                item.Click += (_, _) => { if (TasksList.SelectedItem is TrackedTask t) _vm.MoveTask(t.Id, (Guid)item.Tag); };
        }

        private void DelTask_Click(object sender, RoutedEventArgs e) => _vm.DeleteTask(_vm.SelectedTask);
        private void SaveTasks_Click(object sender, RoutedEventArgs e) => _vm.SaveTasks();

        private void NewProjectBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) _vm.AddProject();
        }

        private void NewTaskBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) _vm.AddTask();
        }

        private void ExportToday_Click(object sender, RoutedEventArgs e) => _vm.ExportCsv(DateTime.Today, DateTime.Today);
        private void ExportWeek_Click(object sender, RoutedEventArgs e) => _vm.ExportCsv(DateTime.Today.AddDays(-6), DateTime.Today);
        private void ExportMonth_Click(object sender, RoutedEventArgs e) =>
            _vm.ExportCsv(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), DateTime.Today);

        private void OpenFolder_Click(object sender, RoutedEventArgs e) =>
            System.Diagnostics.Process.Start("explorer.exe", _vm.DataFolder);
    }
}
