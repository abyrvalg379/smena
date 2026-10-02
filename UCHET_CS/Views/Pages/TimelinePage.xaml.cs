using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using UCHET.Models;
using UCHET.Services;
using UCHET.ViewModels;

namespace UCHET.Views
{
    public partial class TimelinePage : UserControl
    {
        private readonly MainViewModel _vm;

        private static Brush ThemeBrush(string key, string fallback) =>
            ThemeApplier.OptBrush(key) ?? Palette.Frozen(fallback);

        public TimelinePage(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;
            _vm.DataChanged += RenderTimeline;
            _vm.SecondTick += RenderTimeline;
            TimelineCanvas.SizeChanged += (_, _) => RenderTimeline();
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
                double x = (b.Start - start).TotalMinutes / spanMin * w;
                double bw = Math.Max(2.0, (b.End - b.Start).TotalMinutes / spanMin * w);
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
                rect.ToolTip = $"{b.Start:HH:mm}–{b.End:HH:mm} · {_vm.TaskNameFor(b.TaskId)}\n{b.Process}: {b.Title}";
                rect.PreviewMouseLeftButtonDown += Segment_Click;
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

        private void Segment_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is ActivityBlock b)
            {
                var row = _vm.FindRowForBlock(b);
                if (row != null)
                {
                    LogList.SelectedItem = row;
                    LogList.ScrollIntoView(row);
                }
            }
        }

        private static BlockRow? BlockFromEvent(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement fe } } && fe.DataContext is BlockRow r1) return r1;
            return (sender as FrameworkElement)?.DataContext as BlockRow;
        }

        private void MenuBlockAssign_Click(object sender, RoutedEventArgs e)
        {
            if (LogList.SelectedItems.Count > 1) { _vm.BeginAssignBulk(LogList.SelectedItems); return; }
            var row = BlockFromEvent(sender, e);
            if (row != null) _vm.BeginAssign(row);
        }

        private void MenuBlockCollapse_Click(object sender, RoutedEventArgs e)
        {
            if (LogList.SelectedItems.Count > 1) _vm.CollapseSelected(LogList.SelectedItems);
            else { var row = BlockFromEvent(sender, e); if (row != null) _vm.CollapseSelected(new[] { row }); }
        }

        private void MenuBlockEdit_Click(object sender, RoutedEventArgs e)
        {
            var row = BlockFromEvent(sender, e);
            if (row != null) _vm.BeginEditBlock(row);
        }

        private void MenuBlockDelete_Click(object sender, RoutedEventArgs e)
        {
            var row = BlockFromEvent(sender, e);
            if (row != null) { _vm.BeginEditBlock(row); _vm.DeleteEditingBlocks(); }
        }

        private void MenuBlockCopy_Click(object sender, RoutedEventArgs e)
        {
            var row = BlockFromEvent(sender, e);
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
