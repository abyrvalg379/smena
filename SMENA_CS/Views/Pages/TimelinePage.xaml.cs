using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SMENA.Models;
using SMENA.Services;
using SMENA.ViewModels;

namespace SMENA.Views
{
    public partial class TimelinePage : UserControl
    {
        private readonly MainViewModel _vm;

        private static Brush ThemeBrush(string key, string fallback) =>
            ThemeApplier.OptBrush(key) ?? Palette.Frozen(fallback);

        // ---- drag / resize state ----
        private ActivityBlock? _dragBlock;         // block being moved/resized
        private DateTime _dragOrigStart, _dragOrigEnd;
        private DateTime _pendingStart, _pendingEnd;
        private int _dragMode;                     // 0 = move, 1 = resize left, 2 = resize right
        private double _pressX;
        private bool _dragging;                    // past the click threshold
        private const double EdgePx = 7;           // resize grab zone
        private const double MinMinutes = 1;

        public TimelinePage(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;
            _vm.DataChanged += RenderTimeline;
            _vm.SecondTick += RenderTimeline;
            TimelineCanvas.SizeChanged += (_, _) => RenderTimeline();
            TimelineCanvas.MouseMove += Canvas_MouseMove;
            TimelineCanvas.MouseLeftButtonUp += Canvas_Up;
            Loaded += (_, _) => RenderTimeline();
        }

        private void RenderTimeline()
        {
            if (TimelineCanvas.ActualWidth < 20) return;
            var canvas = TimelineCanvas;
            canvas.Children.Clear();
            TimelineLabels.Children.Clear();

            var blocks = _vm.TodayBlocksForTimeline();
            var now = DateTime.Now;
            var (start, end) = Timeline.Window(blocks, now);
            double w = canvas.ActualWidth;
            double spanMin = (end - start).TotalMinutes;

            foreach (var b in blocks)
            {
                // the block being dragged renders with its pending (preview) times
                var (bStart, bEnd) = _dragBlock == b ? (_pendingStart, _pendingEnd) : (b.Start, b.End);
                if (bEnd <= bStart) continue;

                double x = (bStart - start).TotalMinutes / spanMin * w;
                double bw = Math.Max(2.0, (bEnd - bStart).TotalMinutes / spanMin * w);
                var task = b.TaskId == null ? null : _vm.GetTask(b.TaskId.Value);
                var rect = new Rectangle
                {
                    Width = bw,
                    Height = 26,
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = Palette.BrushFor(b.TaskId, task?.ColorHex),
                    Cursor = Cursors.Hand,
                    Tag = b
                };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, 3);
                rect.ToolTip = _dragBlock == b
                    ? $"{_pendingStart:HH:mm}–{_pendingEnd:HH:mm} — release to apply"
                    : (b.IsOpen
                        ? $"{b.Start:HH:mm}–now · tracking in progress"
                        : $"{b.Start:HH:mm}–{b.End:HH:mm} · drag to move, edges to resize");
                if (!b.IsOpen)
                {
                    rect.PreviewMouseLeftButtonDown += Segment_Press;
                    rect.MouseMove += Segment_Hover;
                }
                canvas.Children.Add(rect);
            }

            double nx = (now - start).TotalMinutes / spanMin * w;
            if (nx >= 0 && nx <= w)
            {
                var line = new Rectangle { Width = 1.5, Height = 32, Fill = ThemeBrush("Accent", "#5AC8FA") };
                Canvas.SetLeft(line, nx);
                Canvas.SetTop(line, 0);
                canvas.Children.Add(line);
            }

            double stepHours = (end - start).TotalHours <= 10 ? 1 : 2;
            var t = new DateTime(start.Year, start.Month, start.Day, start.Hour, 0, 0).AddHours(1);
            while (t < end)
            {
                double x = (t - start).TotalMinutes / spanMin * w;
                var tb = new TextBlock { Text = t.ToString("HH:mm"), FontSize = 10, Foreground = ThemeBrush("FgDim", "#8995A5") };
                Canvas.SetLeft(tb, x + 2);
                TimelineLabels.Children.Add(tb);
                t = t.AddHours(stepHours);
            }
        }

        // ---- drag / resize ----

        private void Segment_Press(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not ActivityBlock b || b.IsOpen) return;

            var rect = (Rectangle)sender;
            double x = e.GetPosition(rect).X;
            _dragMode = x <= EdgePx ? 1 : x >= rect.Width - EdgePx ? 2 : 0;
            _dragBlock = b;
            _dragOrigStart = b.Start;
            _dragOrigEnd = b.End;
            _pendingStart = b.Start;
            _pendingEnd = b.End;
            _pressX = e.GetPosition(TimelineCanvas).X;
            _dragging = false;
            TimelineCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void Segment_Hover(object sender, MouseEventArgs e)
        {
            if (_dragBlock != null || (sender as FrameworkElement)?.Tag is not ActivityBlock b || b.IsOpen) return;
            var rect = (Rectangle)sender;
            double x = e.GetPosition(rect).X;
            rect.Cursor = x <= EdgePx || x >= rect.Width - EdgePx ? Cursors.SizeWE : Cursors.Hand;
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragBlock == null || !TimelineCanvas.IsMouseCaptured) return;
            double x = e.GetPosition(TimelineCanvas).X;
            if (!_dragging && Math.Abs(x - _pressX) < 4) return;   // still a click
            _dragging = true;

            double w = TimelineCanvas.ActualWidth;
            var blocks = _vm.TodayBlocksForTimeline();
            var now = DateTime.Now;
            var (start, end) = Timeline.Window(blocks, now);
            double minutesPerPx = spanMinOf(start, end) / w;

            if (_dragMode == 0)
            {
                var delta = TimeSpan.FromMinutes((x - _pressX) * minutesPerPx);
                _pendingStart = _dragOrigStart + delta;
                _pendingEnd = _dragOrigEnd + delta;
            }
            else
            {
                var boundary = start + TimeSpan.FromMinutes(Math.Max(0, x) * minutesPerPx);
                if (_dragMode == 1)
                {
                    _pendingEnd = _dragOrigEnd;
                    _pendingStart = Min(boundary, _pendingEnd.AddMinutes(-MinMinutes));
                }
                else
                {
                    _pendingStart = _dragOrigStart;
                    _pendingEnd = Max(boundary, _pendingStart.AddMinutes(MinMinutes));
                }
            }
            RenderTimeline();
        }

        private void Canvas_Up(object sender, MouseButtonEventArgs e)
        {
            if (_dragBlock == null) return;
            var block = _dragBlock;
            TimelineCanvas.ReleaseMouseCapture();

            if (_dragging)
            {
                block.Start = _pendingStart;
                block.End = _pendingEnd;
                _dragBlock = null;
                _dragging = false;
                _vm.SaveBlocks();          // persist + RefreshAll
                _vm.StatusText = $"Block moved: {block.Start:HH:mm}–{block.End:HH:mm}";
                return;
            }

            // no real drag — behave like the old click: select the row (assign drawer)
            _dragBlock = null;
            var row = _vm.FindRowForBlock(block);
            if (row != null)
            {
                LogList.SelectedItem = row;
                LogList.ScrollIntoView(row);
            }
        }

        private static double spanMinOf(DateTime start, DateTime end) => (end - start).TotalMinutes;
        private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
        private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

        private static BlockRow? BlockRowFromEvent(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement fe } } && fe.DataContext is BlockRow r1) return r1;
            return (sender as FrameworkElement)?.DataContext as BlockRow;
        }

        private void MenuBlockAssign_Click(object sender, RoutedEventArgs e)
        {
            if (LogList.SelectedItems.Count > 1) { _vm.BeginAssignBulk(LogList.SelectedItems); return; }
            var row = BlockRowFromEvent(sender, e);
            if (row != null) _vm.BeginAssign(row);
        }

        private void MenuBlockCollapse_Click(object sender, RoutedEventArgs e)
        {
            if (LogList.SelectedItems.Count > 1) _vm.CollapseSelected(LogList.SelectedItems);
            else { var row = BlockRowFromEvent(sender, e); if (row != null) _vm.CollapseSelected(new[] { row }); }
        }

        private void MenuBlockEdit_Click(object sender, RoutedEventArgs e)
        {
            var row = BlockRowFromEvent(sender, e);
            if (row != null) _vm.BeginEditBlock(row);
        }

        private void MenuBlockDelete_Click(object sender, RoutedEventArgs e)
        {
            var row = BlockRowFromEvent(sender, e);
            if (row != null) { _vm.BeginEditBlock(row); _vm.DeleteEditingBlocks(); }
        }

        private void MenuBlockCopy_Click(object sender, RoutedEventArgs e)
        {
            var row = BlockRowFromEvent(sender, e);
            if (row != null) Clipboard.SetText(row.Title ?? "");
        }

        private void LogList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LogList.SelectedItems.Count > 1)
                _vm.BeginAssignBulk(LogList.SelectedItems);
            else if (e.AddedItems.Count > 0 && e.AddedItems[0] is BlockRow row)
                _vm.BeginAssign(row);
        }
    }
}
