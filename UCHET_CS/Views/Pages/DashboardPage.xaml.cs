using System;
using System.Linq;
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
    public partial class DashboardPage : UserControl
    {
        private readonly MainViewModel _vm;

        public DashboardPage(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;
            _vm.DataChanged += RenderGantt;
            GanttCanvas.SizeChanged += (_, _) => RenderGantt();
            Loaded += (_, _) => RenderGantt();
        }

        private void Pause_Click(object sender, RoutedEventArgs e) => _vm.TogglePause();

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.IsManual) _vm.StopManualNow();
            else _vm.TogglePause();
        }

        private void Assign_Click(object sender, RoutedEventArgs e)
        {
            _vm.StatusText = "Assign works from Timeline / Sessions — click a session there.";
        }

        private void CloseDetails_Click(object sender, RoutedEventArgs e) => _vm.SelectedSession = null;
        private void SessionSaveNote_Click(object sender, RoutedEventArgs e) => _vm.SaveSessionNote();
        private void SessionEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedSession != null) _vm.BeginEditBlock(_vm.SelectedSession);
        }
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

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is string fmt) _vm.ExportReport(fmt);
        }

        private static Brush ThemeBrush(string key, string fallback) =>
            ThemeApplier.OptBrush(key) ?? Palette.Frozen(fallback);

        // ===== per-task gantt rows =====

        private void RenderGantt()
        {
            if (GanttCanvas.ActualWidth < 40) return;
            var canvas = GanttCanvas;
            var labels = GanttLabels;
            canvas.Children.Clear();
            labels.Children.Clear();

            var blocks = _vm.TodayBlocksForTimeline().Where(b => b.End > b.Start).OrderBy(b => b.Start).ToList();
            var now = DateTime.Now;
            var (start, end) = Timeline.Window(blocks, now);
            double w = canvas.ActualWidth;
            double spanMin = (end - start).TotalMinutes;

            var rows = blocks.GroupBy(b => b.TaskId)
                .OrderByDescending(g => g.Sum(b => (b.End - b.Start).TotalMinutes))
                .Select(g =>
                {
                    var t = g.Key == null ? null : _vm.GetTask(g.Key.Value);
                    return new
                    {
                        Name = g.Key == null ? "Other" : _vm.TaskNameFor(g.Key),
                        Brush = Palette.BrushFor(g.Key, t?.ColorHex),
                        Blocks = g.ToList()
                    };
                }).ToList();

            const double rowH = 34;
            const double labelW = 0; // labels drawn in the left panel
            double canvasH = Math.Max(150, rows.Count * rowH + 6);
            canvas.Height = canvasH;

            // labels panel
            foreach (var r in rows)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
                sp.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = r.Brush, VerticalAlignment = VerticalAlignment.Center });
                sp.Children.Add(new TextBlock
                {
                    Text = r.Name,
                    FontSize = 11,
                    Foreground = ThemeBrush("FgDim", "#8995A5"),
                    Margin = new Thickness(6, 0, 0, 0)
                });
                labels.Children.Add(sp);
            }

            // faint row separators
            for (int i = 1; i < rows.Count; i++)
            {
                var sep = new Rectangle { Height = 1, Fill = ThemeBrush("Border", "#161E29") };
                Canvas.SetLeft(sep, 0); Canvas.SetTop(sep, i * rowH);
                sep.Width = w;
                canvas.Children.Add(sep);
            }

            // segments
            foreach (var r in rows)
            {
                int rowIdx = rows.IndexOf(r);
                foreach (var b in r.Blocks)
                {
                    double x = (b.Start - start).TotalMinutes / spanMin * w;
                    double bw = Math.Max(2.0, (b.End - b.Start).TotalMinutes / spanMin * w);
                    var rect = new Rectangle
                    {
                        Width = bw,
                        Height = 15,
                        RadiusX = 3,
                        RadiusY = 3,
                        Fill = r.Brush,
                        Cursor = Cursors.Hand,
                        ToolTip = $"{b.Start:HH:mm}–{b.End:HH:mm} · {_vm.TaskNameFor(b.TaskId)} · {b.Process}"
                    };
                    Canvas.SetLeft(rect, x);
                    Canvas.SetTop(rect, rowIdx * rowH + 6);
                    canvas.Children.Add(rect);
                }
            }

            // now line
            double nx = (now - start).TotalMinutes / spanMin * w;
            if (nx >= 0 && nx <= w)
            {
                var line = new Rectangle { Width = 1.2, Height = canvasH, Fill = ThemeBrush("Accent", "#9AD7F5") };
                Canvas.SetLeft(line, nx);
                Canvas.SetTop(line, 0);
                canvas.Children.Add(line);
            }

            // hour labels above
            double stepHours = (end - start).TotalHours <= 10 ? 1 : 2;
            var t = new DateTime(start.Year, start.Month, start.Day, start.Hour, 0, 0).AddHours(1);
            while (t < end)
            {
                double x = (t - start).TotalMinutes / spanMin * w;
                var tb = new TextBlock { Text = t.ToString("HH:mm"), FontSize = 9, Foreground = ThemeBrush("FgDim", "#6E7887") };
                Canvas.SetLeft(tb, x + 3);
                Canvas.SetTop(tb, -14);
                canvas.Children.Add(tb);
                t = t.AddHours(stepHours);
            }
        }
    }
}
