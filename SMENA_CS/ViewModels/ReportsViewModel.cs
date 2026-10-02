using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Win32;
using SMENA.Models;
using SMENA.Services;

namespace SMENA.ViewModels
{
    /// <summary>Range-based reporting: Project → Task tree, phase rollup, exports.</summary>
    public class ReportsViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        private readonly TaskStore _store;
        private readonly TimeLog _log;

        public static readonly string[] Ranges = { "Today", "Yesterday", "This week", "Last week", "This month", "Custom" };
        public List<string> RangeList => Ranges.ToList();

        public ObservableCollection<TreeRow> TreeRows { get; } = new();
        public ObservableCollection<TreeRow> PhaseRows { get; } = new();
        public ObservableCollection<CalendarDayVM> CalendarDays { get; } = new();

        private DateTime _calMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        private string _calendarTitle = "";
        public string CalendarTitle { get => _calendarTitle; private set { _calendarTitle = value; OnPropertyChanged(nameof(CalendarTitle)); } }
        private string _calendarSummary = "";
        public string CalendarSummary { get => _calendarSummary; private set { _calendarSummary = value; OnPropertyChanged(nameof(CalendarSummary)); } }

        public void CalendarPrev() { _calMonth = _calMonth.AddMonths(-1); RebuildCalendar(); }
        public void CalendarNext() { _calMonth = _calMonth.AddMonths(1); RebuildCalendar(); }

        /// <summary>Heatmap day click → the range picker shows exactly that day.</summary>
        public void CalendarDayClick(DateTime date)
        {
            _customFrom = date.ToString("dd.MM.yyyy");
            _customTo = date.ToString("dd.MM.yyyy");
            OnPropertyChanged(nameof(CustomFrom));
            OnPropertyChanged(nameof(CustomTo));
            _range = "Custom";
            OnPropertyChanged(nameof(Range));
            OnPropertyChanged(nameof(IsCustom));
            Rebuild();
        }

        private string _range = "Today";
        public string Range
        {
            get => _range;
            set { _range = value; OnPropertyChanged(nameof(Range)); OnPropertyChanged(nameof(IsCustom)); Rebuild(); }
        }

        public bool IsCustom => _range == "Custom";

        private string _customFrom = DateTime.Today.ToString("dd.MM.yyyy");
        public string CustomFrom
        {
            get => _customFrom;
            set { _customFrom = value; Rebuild(); }
        }

        private string _customTo = DateTime.Today.ToString("dd.MM.yyyy");
        public string CustomTo
        {
            get => _customTo;
            set { _customTo = value; Rebuild(); }
        }

        private string _rangeTotal = "";
        public string RangeTotal { get => _rangeTotal; set { _rangeTotal = value; OnPropertyChanged(nameof(RangeTotal)); } }

        private bool _hasPhases;
        public bool HasPhases { get => _hasPhases; set { _hasPhases = value; OnPropertyChanged(nameof(HasPhases)); } }

        public ReportsViewModel(TaskStore store, TimeLog log)
        {
            _store = store;
            _log = log;
            Rebuild();
        }

        public (DateTime From, DateTime To) ComputeRange()
        {
            var today = DateTime.Today;
            switch (_range)
            {
                case "Yesterday": return (today.AddDays(-1), today);
                case "This week":
                    var mon = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
                    return (mon, mon.AddDays(7));
                case "Last week":
                    var mon2 = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)).AddDays(-7);
                    return (mon2, mon2.AddDays(7));
                case "This month":
                    var first = new DateTime(today.Year, today.Month, 1);
                    return (first, first.AddMonths(1));
                case "Custom":
                    var okFrom = DateTime.TryParseExact((_customFrom ?? "").Trim(), "dd.MM.yyyy", null,
                        System.Globalization.DateTimeStyles.None, out var f);
                    var okTo = DateTime.TryParseExact((_customTo ?? "").Trim(), "dd.MM.yyyy", null,
                        System.Globalization.DateTimeStyles.None, out var t);
                    if (okFrom && okTo && t >= f) return (f, t.AddDays(1));
                    return (today, today.AddDays(1));
                default: return (today, today.AddDays(1));   // Today
            }
        }

        public void Rebuild()
        {
            var (from, to) = ComputeRange();
            var blocks = _log.Blocks
                .Where(b => b.End > from && b.Start < to && !b.IsOpen)
                .ToList();

            var tree = Aggregation.BuildTree(blocks, _store.Projects, _store.Tasks, includeArchived: true);
            var total = tree.Sum(n => n.Minutes);
            RangeTotal = FormatSpan(TimeSpan.FromMinutes(total));

            TreeRows.Clear();
            foreach (var node in tree)
            {
                TreeRows.Add(new TreeRow
                {
                    Name = node.Name + (node.IsArchived ? "  (closed)" : ""),
                    MinutesText = FormatSpan(TimeSpan.FromMinutes(node.Minutes)),
                    Percent = total < 1 ? 0 : Math.Min(100, node.Minutes / total * 100.0),
                    Bar = ProjectBrush(node),
                    IsProject = node.IsProject,
                    IsUnsorted = node.IsUnsorted,
                });
                foreach (var child in node.Children)
                {
                    TreeRows.Add(new TreeRow
                    {
                        Name = "      " + child.Name + (child.IsArchived ? "  (archived)" : ""),
                        MinutesText = FormatSpan(TimeSpan.FromMinutes(child.Minutes)),
                        Percent = total < 1 ? 0 : Math.Min(100, child.Minutes / total * 100.0),
                        Bar = ProjectBrush(child),
                        Phase = child.Phase,
                    });
                }
            }

            PhaseRows.Clear();
            foreach (var ph in Aggregation.PhaseRollup(blocks, _store.Tasks, includeArchived: true))
            {
                PhaseRows.Add(new TreeRow
                {
                    Name = ph.Name,
                    MinutesText = FormatSpan(TimeSpan.FromMinutes(ph.Minutes)),
                    Percent = total < 1 ? 0 : Math.Min(100, ph.Minutes / total * 100.0),
                    Bar = SMENA.Services.Palette.Frozen("#FFD60A"),
                });
            }
            HasPhases = PhaseRows.Count > 0;
            RebuildCalendar();
        }

        private void RebuildCalendar()
        {
            var m = Calendar.Build(_calMonth.Year, _calMonth.Month, _log.Blocks);
            var max = Math.Max(1, m.Days.Where(d => d.InMonth).Max(d => d.Minutes));
            var today = DateTime.Today;
            CalendarDays.Clear();
            foreach (var d in m.Days)
            {
                CalendarDays.Add(new CalendarDayVM
                {
                    Date = d.Date,
                    Minutes = d.Minutes,
                    MinutesText = FormatSpan(TimeSpan.FromMinutes(d.Minutes)),
                    Cell = HeatBrush(d.Minutes, max),
                    InMonth = d.InMonth,
                    IsToday = d.Date == today,
                });
            }
            CalendarTitle = _calMonth.ToString("MMMM yyyy");
            var avg = m.ActiveDays > 0 ? m.TotalMinutes / m.ActiveDays : 0;
            CalendarSummary = $"{FormatSpan(TimeSpan.FromMinutes(m.TotalMinutes))} · {m.ActiveDays} active days · avg {FormatSpan(TimeSpan.FromMinutes(avg))}" +
                              (m.BestDayDate != null ? $" · best {FormatSpan(TimeSpan.FromMinutes(m.BestDayMinutes))} on {m.BestDayDate:dd.MM}" : "");
        }

        /// <summary>Accent color with alpha scaled by sqrt(intensity) — low days stay visible; zero = track.</summary>
        private static System.Windows.Media.Brush HeatBrush(int minutes, int maxMinutes)
        {
            System.Windows.Media.Color accent =
                (ThemeApplier.OptBrush("Accent") as System.Windows.Media.SolidColorBrush)?.Color
                ?? System.Windows.Media.Color.FromRgb(0x30, 0xD1, 0x58);
            var track = ThemeApplier.OptBrush("Track") ?? System.Windows.Media.Brushes.Transparent;
            if (minutes <= 0) return track;
            var t = 0.30 + 0.70 * Math.Sqrt(minutes / (double)maxMinutes);
            var brush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb((byte)(255 * t), accent.R, accent.G, accent.B));
            brush.Freeze();
            return brush;
        }

        private static System.Windows.Media.Brush ProjectBrush(AggNode node)
        {
            if (node.IsProject || node.IsUnsorted)
                return SMENA.Services.Palette.Frozen(string.IsNullOrEmpty(node.ColorHex) ? "#5AC8FA" : node.ColorHex);
            return SMENA.Services.Palette.Frozen(SMENA.Services.Palette.HexFor(node.TaskId, node.ColorHex));
        }

        private List<(string Project, string Task, string Phase, double Minutes)> FlatForRange()
        {
            var (from, to) = ComputeRange();
            var blocks = _log.Blocks
                .Where(b => b.End > from && b.Start < to && !b.IsOpen)
                .ToList();
            return Aggregation.FlatRows(blocks, _store.Projects, _store.Tasks);
        }

        public void Export(string format)
        {
            var dlg = new SaveFileDialog { FileName = $"smena_report_{DateTime.Today:yyyyMMdd}.{format.ToLower()}" };
            switch (format.ToLower())
            {
                case "csv": dlg.Filter = "CSV|*.csv"; break;
                case "json": dlg.Filter = "JSON|*.json"; break;
                case "md": dlg.Filter = "Markdown|*.md"; break;
                case "html": dlg.Filter = "HTML|*.html"; break;
                default: return;
            }
            if (dlg.ShowDialog() != true) return;

            var rows = FlatForRange();
            switch (format.ToLower())
            {
                case "csv": Exporters.WriteTreeCsv(dlg.FileName, rows); break;
                case "json": Exporters.WriteTreeJson(dlg.FileName, rows); break;
                case "md": Exporters.WriteTreeMd(dlg.FileName, rows, _range); break;
                case "html": Exporters.WriteTreeHtml(dlg.FileName, rows, _range); break;
            }
        }

        private static string FormatSpan(TimeSpan s) =>
            s.TotalHours >= 1 ? $"{(int)s.TotalHours}h {s.Minutes:00}m" : $"{s.Minutes}m";

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));
    }
}
