using System;
using System.Collections.Generic;
using System.Linq;
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
    public partial class DashboardPage : UserControl
    {
        private readonly MainViewModel _vm;

        private static Brush ThemeBrush(string key, string fallback) =>
            ThemeApplier.OptBrush(key) ?? Palette.Frozen(fallback);

        public DashboardPage(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;
            _vm.DataChanged += RenderGantt;
            GanttCanvas.SizeChanged += (_, _) => RenderGantt();
            GanttCanvas.MouseMove += GanttCanvas_MouseMove;
            GanttCanvas.MouseLeftButtonUp += GanttCanvas_MouseUp;
            Loaded += (_, _) => RenderGantt();
        }

        private void Pause_Click(object sender, RoutedEventArgs e) => _vm.TogglePause();

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.IsManual) _vm.StopManualNow();
            else _vm.TogglePause();
        }

        private void Reports_Click(object sender, RoutedEventArgs e) => _vm.NavigateTo("Reports");

        /// <summary>The "…" button: assign the block that is being tracked right now.</summary>
        private void AssignOpenBlock_Click(object sender, RoutedEventArgs e)
        {
            var open = _vm.TodayBlocksForTimeline().LastOrDefault(b => b.IsOpen);
            if (open == null)
            {
                _vm.StatusText = "Nothing is being tracked right now.";
                return;
            }
            var row = _vm.FindRowForBlock(open) ?? new BlockRow
            {
                Blocks = new List<ActivityBlock> { open },
                TimeRange = $"{open.Start:HH:mm}–{open.End:HH:mm}",
                App = open.Process,
                Title = open.Title ?? ""
            };
            _vm.BeginAssign(row);
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

        // ===== per-project gantt: project sections, task rows, idle row =====

        // ---- drag / resize state (same interaction as the Timeline day strip) ----
        private ActivityBlock? _dragBlock;
        private DateTime _dragOrigStart, _dragOrigEnd;
        private DateTime _pendingStart, _pendingEnd;
        private int _dragMode;
        private double _pressX;
        private bool _dragging;
        private const double EdgePx = 7;
        private const double MinMinutes = 1;

        private sealed class GanttRow
        {
            public string Name = "";
            public Brush Brush = Brushes.Gray;
            public bool Header;
            public bool Dim;
            public List<ActivityBlock> Blocks = new();
        }

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
            if (spanMin <= 0) return;

            var tasks = _vm.Store.Tasks;
            var tree = Aggregation.BuildTree(blocks, _vm.Store.Projects, tasks, includeArchived: false);

            var rows = new List<GanttRow>();
            foreach (var proj in tree.Where(p => p.IsProject))
            {
                rows.Add(new GanttRow { Name = proj.Name, Brush = ProjectBrushOf(proj), Header = true });
                foreach (var child in proj.Children)
                {
                    rows.Add(new GanttRow
                    {
                        Name = child.Name,
                        Brush = Palette.Frozen(Palette.HexFor(child.TaskId, child.ColorHex)),
                        Blocks = blocks.Where(b => b.TaskId == child.TaskId).ToList()
                    });
                }
            }
            var unsorted = tree.FirstOrDefault(n => n.IsUnsorted);
            if (unsorted != null)
                rows.Add(new GanttRow
                {
                    Name = "Unsorted",
                    Brush = ThemeBrush("UnsortedBrush", "#5A5A64"),
                    Dim = true,
                    Blocks = blocks.Where(b => b.TaskId == null || tasks.All(t => t.Id != b.TaskId)).ToList()
                });

            // idle: gaps inside the observed window (>= 5 min)
            var idle = new List<ActivityBlock>();
            var cursor = blocks.Count > 0 ? blocks[0].Start : start;
            foreach (var b in blocks)
            {
                if ((b.Start - cursor).TotalMinutes >= 5)
                    idle.Add(new ActivityBlock { Start = cursor, End = b.Start, Process = "idle" });
                if (b.End > cursor) cursor = b.End;
            }
            var idleEnd = end < now ? end : now;
            if ((idleEnd - cursor).TotalMinutes >= 5)
                idle.Add(new ActivityBlock { Start = cursor, End = idleEnd, Process = "idle" });
            if (idle.Count > 0)
                rows.Add(new GanttRow { Name = "IDLE", Brush = ThemeBrush("IdleBrush", "#4A4A52"), Dim = true, Blocks = idle });

            const double rowH = 28;
            double canvasH = Math.Max(120, rows.Count * rowH + 6);
            canvas.Height = canvasH;

            int rowIdx = 0;
            foreach (var r in rows)
            {
                double top = rowIdx * rowH + 4;

                var label = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, top - 2, 0, 0) };
                if (!r.Header)
                    label.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = r.Brush, VerticalAlignment = VerticalAlignment.Center });
                label.Children.Add(new TextBlock
                {
                    Text = r.Name,
                    FontSize = r.Header ? 11.5 : 11,
                    FontWeight = r.Header ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = r.Header ? ThemeBrush("Fg", "#E8EDF3") : ThemeBrush("FgDim", "#8995A5"),
                    Margin = new Thickness(r.Header ? 0 : 6, 0, 0, 0)
                });
                Canvas.SetLeft(label, 0);
                Canvas.SetTop(label, top);
                labels.Children.Add(label);

                if (!r.Header)
                {
                    if (rowIdx > 0)
                    {
                        var sep = new Rectangle { Height = 1, Fill = ThemeBrush("Border", "#263241"), Opacity = 0.5 };
                        Canvas.SetLeft(sep, 0); Canvas.SetTop(sep, rowIdx * rowH);
                        sep.Width = w;
                        canvas.Children.Add(sep);
                    }

                    foreach (var b in r.Blocks)
                    {
                        var (bStart, bEnd) = _dragBlock == b ? (_pendingStart, _pendingEnd) : (b.Start, b.End);
                        double x = Math.Max(0, (bStart - start).TotalMinutes / spanMin * w);
                        double xEnd = Math.Min(w, (bEnd - start).TotalMinutes / spanMin * w);
                        double bw = Math.Max(2.0, xEnd - x);
                        var brush = r.Dim && b.Process == "idle" ? ThemeBrush("IdleBrush", "#4A4A52") : r.Brush;
                        var rect = new Rectangle
                        {
                            Width = bw,
                            Height = 14,
                            RadiusX = 3,
                            RadiusY = 3,
                            Fill = brush,
                            Cursor = Cursors.Hand,
                            Tag = b,
                            ToolTip = _dragBlock == b
                                ? $"{_pendingStart:HH:mm}–{_pendingEnd:HH:mm} — release to apply"
                                : $"{b.Start:HH:mm}–{b.End:HH:mm} · {_vm.TaskNameFor(b.TaskId)} · {b.Process}"
                        };
                        if (b.Process != "idle")
                        {
                            rect.Tag = b;
                            rect.PreviewMouseLeftButtonDown += GanttSegment_Press;
                            rect.MouseMove += GanttSegment_Hover;
                        }
                        Canvas.SetLeft(rect, x);
                        Canvas.SetTop(rect, top);
                        canvas.Children.Add(rect);
                    }
                }
                rowIdx++;
            }

            // now line
            double nx = (now - start).TotalMinutes / spanMin * w;
            if (nx >= 0 && nx <= w)
            {
                var line = new Rectangle { Width = 1.2, Height = canvasH, Fill = ThemeBrush("Accent", "#5AC8FA") };
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

        private static Brush ProjectBrushOf(AggNode node) =>
            Palette.Frozen(string.IsNullOrEmpty(node.ColorHex) ? "#5AC8FA" : node.ColorHex);

        private void GanttSegment_Press(object sender, MouseButtonEventArgs e)
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
            _pressX = e.GetPosition(GanttCanvas).X;
            _dragging = false;
            GanttCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void GanttSegment_Hover(object sender, MouseEventArgs e)
        {
            if (_dragBlock != null || (sender as FrameworkElement)?.Tag is not ActivityBlock b || b.IsOpen) return;
            var rect = (Rectangle)sender;
            double x = e.GetPosition(rect).X;
            rect.Cursor = x <= EdgePx || x >= rect.Width - EdgePx ? Cursors.SizeWE : Cursors.Hand;
        }

        private void GanttCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragBlock == null || !GanttCanvas.IsMouseCaptured) return;
            double x = e.GetPosition(GanttCanvas).X;
            if (!_dragging && Math.Abs(x - _pressX) < 4) return;
            _dragging = true;

            double w = GanttCanvas.ActualWidth;
            var blocks = _vm.TodayBlocksForTimeline().Where(b => b.End > b.Start).OrderBy(b => b.Start).ToList();
            var (start, end) = Timeline.Window(blocks, DateTime.Now);
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
            RenderGantt();
        }

        private void GanttCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragBlock == null) return;
            var block = _dragBlock;
            GanttCanvas.ReleaseMouseCapture();

            if (_dragging)
            {
                block.Start = _pendingStart;
                block.End = _pendingEnd;
                _dragBlock = null;
                _dragging = false;
                _vm.SaveBlocks();
                _vm.StatusText = $"Block moved: {block.Start:HH:mm}–{block.End:HH:mm}";
                return;
            }

            _dragBlock = null;
            var row = _vm.FindRowForBlock(block) ?? new BlockRow
            {
                Blocks = new List<ActivityBlock> { block },
                TimeRange = $"{block.Start:HH:mm}–{block.End:HH:mm}",
                App = block.Process,
                Title = block.Title ?? ""
            };
            _vm.BeginAssign(row);
        }

        private static double spanMinOf(DateTime start, DateTime end) => (end - start).TotalMinutes;
        private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
        private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
    }
}
