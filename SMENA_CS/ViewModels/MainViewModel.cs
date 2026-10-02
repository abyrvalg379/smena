using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32;
using SMENA.Models;
using SMENA.Services;

namespace SMENA.ViewModels
{
    public class BlockRow
    {
        public string TimeRange { get; set; } = "";
        public string Duration { get; set; } = "";
        public string App { get; set; } = "";
        public string Task { get; set; } = "";
        public string Title { get; set; } = "";

        /// <summary>Underlying blocks this display row was merged from (assignment targets).</summary>
        public List<ActivityBlock> Blocks { get; set; } = new();

        public string Info => $"{TimeRange} · {App} — {Title}";
    }

    public class BarRow
    {
        public string Name { get; set; } = "";
        public System.Windows.Media.Brush Bar { get; set; } = System.Windows.Media.Brushes.Gray;
        public string MinutesText { get; set; } = "";
        public double Percent { get; set; }   // 0..100 → bar width
    }

    /// <summary>Row of the dashboard Today tree: project rows and their task children.</summary>
    public class TreeRow
    {
        public string Name { get; set; } = "";
        public string MinutesText { get; set; } = "";
        public double Percent { get; set; }
        public System.Windows.Media.Brush Bar { get; set; } = System.Windows.Media.Brushes.Gray;
        public string Phase { get; set; } = "";
        public bool IsProject { get; set; }
        public bool IsUnsorted { get; set; }
        public Guid? TaskId { get; set; }
        public bool CanClose => !IsProject && !IsUnsorted && TaskId != null;
    }

    public class AppRow
    {
        public string Name { get; set; } = "";
        public string DurationText { get; set; } = "";
        public double Percent { get; set; }
    }

    public class NoteRow
    {
        public string TaskName { get; set; } = "";
        public System.Windows.Media.Brush Brush { get; set; } = System.Windows.Media.Brushes.Gray;
        public string MinutesText { get; set; } = "";
        public string NoteText { get; set; } = "";
    }

    public class ProjectMiniRow
    {
        public string Name { get; set; } = "";
        public string HoursText { get; set; } = "";
        public double Percent { get; set; }
    }

    public class MainViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        private readonly TaskStore _store;
        private readonly TimeLog _log;
        private readonly ConfigManager _config;
        private readonly Poller _poller;
        private readonly DispatcherTimer _uiTicker;
        private readonly DispatcherTimer _refreshTicker;
        private readonly Dictionary<Guid?, TaskTimerRow> _rowMap = new();
        private Guid? _activeId;
        private DateTime? _activeSessionStart;

        public ObservableCollection<BlockRow> TodayRows { get; } = new();
        public ObservableCollection<TaskTimerRow> TimerRows { get; } = new();
        public ObservableCollection<WeekGridRow> WeekGrid { get; } = new();
        public ObservableCollection<TreeRow> TodayBars { get; } = new();
        public ReportsViewModel Reports { get; }
        public JournalViewModel Journal { get; }
        public ObservableCollection<BlockRow> RecentRows { get; } = new();
        public ObservableCollection<AppRow> AppRows { get; } = new();

        // ---- selected day (navigator) ----

        private DateTime _selectedDate = DateTime.Today;
        public DateTime SelectedDate
        {
            get => _selectedDate;
            set
            {
                _selectedDate = value.Date;
                OnPropertyChanged(nameof(SelectedDate));
                OnPropertyChanged(nameof(SelectedDateLabel));
                RebuildDay();
                RebuildVisuals();
                RebuildApplications();
                DataChanged?.Invoke();
            }
        }

        public string SelectedDateLabel =>
            SelectedDate == DateTime.Today ? $"Today, {SelectedDate:dd MMM yyyy}" : SelectedDate.ToString("ddd, dd MMM yyyy");

        public void PrevDay() => SelectedDate = SelectedDate.AddDays(-1);
        public void NextDay() => SelectedDate = SelectedDate.AddDays(1);

        private string _statTotal = "";
        public string StatTotal { get => _statTotal; set { _statTotal = value; OnPropertyChanged(nameof(StatTotal)); } }

        private string _statAvg = "";
        public string StatAvg { get => _statAvg; set { _statAvg = value; OnPropertyChanged(nameof(StatAvg)); } }

        private string _statLongest = "";
        public string StatLongest { get => _statLongest; set { _statLongest = value; OnPropertyChanged(nameof(StatLongest)); } }

        // ---- TIMETRACK visuals ----

        private string _legendWorking = "0m";
        public string LegendWorking { get => _legendWorking; set { _legendWorking = value; OnPropertyChanged(nameof(LegendWorking)); } }

        private string _legendIdle = "0m";
        public string LegendIdle { get => _legendIdle; set { _legendIdle = value; OnPropertyChanged(nameof(LegendIdle)); } }

        private string _legendUnsorted = "0m";
        public string LegendUnsorted { get => _legendUnsorted; set { _legendUnsorted = value; OnPropertyChanged(nameof(LegendUnsorted)); } }

        private List<Controls.DonutItem> _donutItems = new();
        public List<Controls.DonutItem> DonutItems { get => _donutItems; set { _donutItems = value; OnPropertyChanged(nameof(DonutItems)); } }

        private string _workingTaskName = "";
        public string WorkingTaskName { get => _workingTaskName; set { _workingTaskName = value; OnPropertyChanged(nameof(WorkingTaskName)); } }

        private string _workingProjectName = "";
        public string WorkingProjectName { get => _workingProjectName; set { _workingProjectName = value; OnPropertyChanged(nameof(WorkingProjectName)); } }

        public ObservableCollection<NoteRow> NotesItems { get; } = new();
        public ObservableCollection<ProjectMiniRow> ProjectMinis { get; } = new();
        public ObservableCollection<BarRow> ReportBars { get; } = new();

        private string _weekLabel = "";
        public string WeekLabel { get => _weekLabel; set { _weekLabel = value; OnPropertyChanged(nameof(WeekLabel)); } }

        private string _weekTotal = "";
        public string WeekTotal { get => _weekTotal; set { _weekTotal = value; OnPropertyChanged(nameof(WeekTotal)); } }

        private string _weekAvg = "";
        public string WeekAvg { get => _weekAvg; set { _weekAvg = value; OnPropertyChanged(nameof(WeekAvg)); } }

        public void ExportReport(string format)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = $"smena_report_{DateTime.Today:yyyyMMdd}.{format.ToLower()}" };
            switch (format.ToLower())
            {
                case "csv": dlg.Filter = "CSV|*.csv"; break;
                case "json": dlg.Filter = "JSON|*.json"; break;
                case "md": dlg.Filter = "Markdown|*.md"; break;
                case "html": dlg.Filter = "HTML|*.html"; break;
                default: return;
            }
            if (dlg.ShowDialog() != true) return;
            var weekStart = DateTime.Today.AddDays(-6);
            var blocks = RangeBlocks(weekStart, DateTime.Today.AddDays(1)).ToList();
            Func<Guid?, (string Task, string? Project)> names = id =>
            {
                if (id == null) return ("Unsorted", "");
                var t = _store.Tasks.FirstOrDefault(x => x.Id == id);
                return (t?.Name ?? "Deleted task", _store.ProjectName(t?.ProjectId ?? Guid.Empty));
            };
            switch (format.ToLower())
            {
                case "csv": Services.Exporters.WriteCsv(dlg.FileName, blocks, names); break;
                case "json": Services.Exporters.WriteJson(dlg.FileName, blocks, names); break;
                case "md": Services.Exporters.WriteMarkdown(dlg.FileName, "Week report", blocks, names); break;
                case "html": Services.Exporters.WriteHtml(dlg.FileName, "Week report", blocks, names); break;
            }
            StatusText = $"Exported: {dlg.FileName}";
        }

        private void RebuildVisuals()
        {
            var day = SelectedDate;
            var blocks = RangeBlocks(day, day.AddDays(1)).Where(b => b.End > day).OrderBy(b => b.Start).ToList();
            var total = blocks.Sum(b => (b.End - b.Start).TotalMinutes);
            LegendWorking = FormatSpan(TimeSpan.FromMinutes(total));
            LegendUnsorted = FormatSpan(TimeSpan.FromMinutes(
                blocks.Where(b => b.TaskId == null).Sum(b => (b.End - b.Start).TotalMinutes)));

            // honest idle: gaps inside the observed window (first block start → day end/now)
            double idle = 0;
            if (blocks.Count > 0)
            {
                var observedEnd = day == DateTime.Today ? DateTime.Now : day.AddDays(1);
                var observed = (observedEnd - blocks[0].Start).TotalMinutes;
                idle = Math.Max(0, observed - total);
            }
            LegendIdle = FormatSpan(TimeSpan.FromMinutes(idle));

            DonutItems = Aggregation.BuildTree(blocks, _store.Projects, _store.Tasks, includeArchived: false)
                .Select(n => new Controls.DonutItem
                {
                    Value = n.Minutes,
                    Brush = ProjectBrush(n)
                })
                .Where(d => d.Value > 0)
                .ToList();

            var open = _log.Blocks.LastOrDefault(b => b.IsOpen);
            if (open != null)
            {
                var t = open.TaskId == null ? null : _store.Tasks.FirstOrDefault(x => x.Id == open.TaskId);
                WorkingTaskName = t?.Name ?? "Unsorted";
                WorkingProjectName = _store.ProjectName(t?.ProjectId ?? Guid.Empty) ?? "";
            }
            else
            {
                WorkingTaskName = "";
                WorkingProjectName = "";
            }

            // Notes: blocks with notes grouped by task
            NotesItems.Clear();
            foreach (var g in blocks.Where(b => !string.IsNullOrWhiteSpace(b.Note)).GroupBy(b => b.TaskId))
            {
                var t = g.Key == null ? null : _store.Tasks.FirstOrDefault(x => x.Id == g.Key);
                var mins = g.Sum(b => (b.End - b.Start).TotalMinutes);
                NotesItems.Add(new NoteRow
                {
                    TaskName = t?.Name ?? "Unsorted",
                    Brush = Palette.BrushFor(g.Key, t?.ColorHex),
                    MinutesText = FormatSpan(TimeSpan.FromMinutes(mins)),
                    NoteText = string.Join("\n", g.Select(b => b.Note).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct())
                });
            }

            // projects mini (selected day)
            ProjectMinis.Clear();
            var projGroups = blocks.GroupBy(b =>
            {
                if (b.TaskId == null) return "Unsorted";
                var t = _store.Tasks.FirstOrDefault(x => x.Id == b.TaskId);
                return _store.ProjectName(t?.ProjectId ?? Guid.Empty) ?? "Unsorted";
            }).OrderByDescending(g => g.Sum(b => (b.End - b.Start).TotalMinutes)).ToList();
            var projTotal = projGroups.Sum(g => g.Sum(b => (b.End - b.Start).TotalMinutes));
            foreach (var g in projGroups)
            {
                var mins = g.Sum(b => (b.End - b.Start).TotalMinutes);
                ProjectMinis.Add(new ProjectMiniRow
                {
                    Name = g.Key,
                    HoursText = FormatSpan(TimeSpan.FromMinutes(mins)),
                    Percent = projTotal < 1 ? 0 : Math.Min(100, mins / projTotal * 100.0)
                });
            }

            // week report mini
            var weekStart = day.AddDays(-6);
            var weekBlocks = RangeBlocks(weekStart, day.AddDays(1)).ToList();
            var cal = System.Globalization.CultureInfo.InvariantCulture.Calendar;
            var wk = cal.GetWeekOfYear(DateTime.Today, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
            WeekLabel = $"Week {wk} ({weekStart:dd MMM} – {day:dd MMM})";
            var wTotal = weekBlocks.Sum(b => (b.End - b.Start).TotalMinutes);
            WeekTotal = FormatSpan(TimeSpan.FromMinutes(wTotal));
            WeekAvg = FormatSpan(TimeSpan.FromMinutes(wTotal / 7.0));
            ReportBars.Clear();
            var wProj = weekBlocks.GroupBy(b =>
            {
                if (b.TaskId == null) return "Other";
                var t = _store.Tasks.FirstOrDefault(x => x.Id == b.TaskId);
                return _store.ProjectName(t?.ProjectId ?? Guid.Empty) ?? "Other";
            }).OrderByDescending(g => g.Sum(b => (b.End - b.Start).TotalMinutes)).ToList();
            foreach (var g in wProj)
            {
                var mins = g.Sum(b => (b.End - b.Start).TotalMinutes);
                ReportBars.Add(new BarRow
                {
                    Name = g.Key,
                    MinutesText = FormatSpan(TimeSpan.FromMinutes(mins)),
                    Percent = wTotal < 1 ? 0 : Math.Min(100, mins / wTotal * 100.0),
                    Bar = Palette.Frozen("#5AC8FA")
                });
            }
        }

        private string[] _weekDayHeaders = Array.Empty<string>();
        public string[] WeekDayHeaders { get => _weekDayHeaders; private set { _weekDayHeaders = value; OnPropertyChanged(nameof(WeekDayHeaders)); } }
        public ObservableCollection<Project> Projects { get; } = new();
        public ObservableCollection<TrackedTask> SelectedProjectTasks { get; } = new();

        private Project? _selectedProject;
        public Project? SelectedProject
        {
            get => _selectedProject;
            set { _selectedProject = value; OnPropertyChanged(nameof(SelectedProject)); RebuildTaskList(); }
        }

        private TrackedTask? _selectedTask;
        public TrackedTask? SelectedTask
        {
            get => _selectedTask;
            set { _selectedTask = value; OnPropertyChanged(nameof(SelectedTask)); }
        }

        public string NewProjectName { get; set; } = "";
        public string NewTaskName { get; set; } = "";
        public string NewTaskKeywords { get; set; } = "";

        private string _statusText = "";
        public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(nameof(StatusText)); } }

        private string _currentApp = "";
        public string CurrentApp { get => _currentApp; set { _currentApp = value; OnPropertyChanged(nameof(CurrentApp)); } }

        private string _currentFile = "";
        public string CurrentFile { get => _currentFile; set { _currentFile = value; OnPropertyChanged(nameof(CurrentFile)); } }

        private BlockRow? _selectedSession;
        public BlockRow? SelectedSession
        {
            get => _selectedSession;
            set
            {
                _selectedSession = value;
                OnPropertyChanged(nameof(SelectedSession));
                OnPropertyChanged(nameof(SessionDetailsVisible));
                OnPropertyChanged(nameof(SessionNote));
                OnPropertyChanged(nameof(SessionBrush));
                OnPropertyChanged(nameof(SessionTaskLabel));
            }
        }

        public string SessionTaskLabel => SelectedSession?.Task ?? "";

        private TaskTimerRow? _selectedQuickTask;
        public TaskTimerRow? SelectedQuickTask
        {
            get => _selectedQuickTask;
            set { _selectedQuickTask = value; OnPropertyChanged(nameof(SelectedQuickTask)); }
        }

        public string AutoModeLabel => _config.Current.AutoMode ? "ON" : "OFF";

        public void SaveSessionNote()
        {
            if (SelectedSession == null) return;
            var note = SelectedSession.Blocks.FirstOrDefault()?.Note ?? "";
            foreach (var b in SelectedSession.Blocks) b.Note = note;
            _log.Save();
        }

        public void StartTrackingQuick()
        {
            var row = SelectedQuickTask;
            if (row != null && row.TaskId != null)
            {
                ToggleManualFor(row.TaskId);
                return;
            }
            var any = _store.Tasks.FirstOrDefault();
            if (any == null) { StatusText = "No tasks yet — add one first."; return; }
            ToggleManualFor(any.Id);
        }

        public bool SessionDetailsVisible => SelectedSession != null;

        public string SessionNote
        {
            get => SelectedSession?.Blocks.FirstOrDefault()?.Note ?? "";
            set
            {
                if (SelectedSession == null) return;
                foreach (var b in SelectedSession.Blocks) b.Note = value ?? "";
            }
        }

        public System.Windows.Media.Brush SessionBrush => SelectedSession == null
            ? System.Windows.Media.Brushes.Gray
            : Palette.BrushFor(SelectedSession.Blocks[0].TaskId,
                _store.Tasks.FirstOrDefault(t => t.Id == SelectedSession.Blocks[0].TaskId)?.ColorHex);

        private string _todayTotal = "";
        public string TodayTotal { get => _todayTotal; set { _todayTotal = value; OnPropertyChanged(nameof(TodayTotal)); } }

        private string _heroTime = "00:00:00";
        public string HeroTime { get => _heroTime; set { _heroTime = value; OnPropertyChanged(nameof(HeroTime)); } }

        private string _statusLine = "";
        public string StatusLine { get => _statusLine; set { _statusLine = value; OnPropertyChanged(nameof(StatusLine)); } }

        private bool _isRunning;
        public bool IsRunning { get => _isRunning; set { _isRunning = value; OnPropertyChanged(nameof(IsRunning)); } }

        private TrackStatus? _lastStatus;

        private string _manualButtonText = "START MANUAL";
        public string ManualButtonText { get => _manualButtonText; set { _manualButtonText = value; OnPropertyChanged(nameof(ManualButtonText)); } }

        public string DataFolder { get; }

        public TaskStore Store => _store;
        public TimeLog Log => _log;

        public event Action<string>? NavigateRequested;
        public void NavigateTo(string pageKey) => NavigateRequested?.Invoke(pageKey);
        public bool IsManual => _poller.ManualMode;
        public bool IsPaused => _poller.IsPaused;

        public void TogglePause()
        {
            _poller.SetPaused(!_poller.IsPaused);
            OnPropertyChanged(nameof(IsPaused));
            RefreshStatus(null);
            RefreshAll();
        }

        public event Action? DataChanged;
        public event Action? SecondTick;

        private TrackState _lastState = (TrackState)(-1);
        private int _lastBlockCount = -1;

        public MainViewModel(TaskStore store, TimeLog log, ConfigManager config, Poller poller)
        {
            _store = store;
            _log = log;
            _config = config;
            _poller = poller;

            DataFolder = Path.GetDirectoryName(_log.FilePath) ?? "";

            poller.StatusChanged += OnStatus;
            poller.IdleReturned += OnIdleReturned;
            ThemeApplier.ThemeChanged += RefreshAll;   // canvases re-render from fresh data

            _uiTicker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _uiTicker.Tick += (_, _) => OnSecondTick();
            _uiTicker.Start();

            _refreshTicker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _refreshTicker.Tick += (_, _) => RefreshAll();
            _refreshTicker.Start();

            Reports = new ReportsViewModel(store, log);
            Journal = new JournalViewModel(store, log);

            ReloadProjects();
            RefreshStatus(null);
            RefreshAll();
            CheckForUpdates(silent: true);
        }

        private void OnStatus(TrackStatus s)
        {
            _lastStatus = s;
            RefreshStatus(s);
            var count = _log.Blocks.Count;
            if (s.State != _lastState || count != _lastBlockCount)
            {
                _lastState = s.State;
                _lastBlockCount = count;
                RefreshAll();
            }
        }

        // ---- status / manual ----

        public void ToggleManual()
        {
            var task = SelectedTask ?? SelectedProjectTasks.FirstOrDefault();
            ToggleManualFor(task?.Id);
        }

        public void StopManualNow()
        {
            if (!_poller.ManualMode) return;
            _poller.StopManual();
            OnPropertyChanged(nameof(IsManual));
            UpdateManualButton();
            RefreshStatus(null);
            RefreshAll();
        }

        /// <summary>Split the selected session's blocks at their midpoints (two halves each).</summary>
        public void SplitSession()
        {
            if (SelectedSession?.Blocks is not { Count: > 0 } blocks) return;
            int idx = _log.Blocks.IndexOf(blocks[0]);
            if (idx < 0) return;

            var insert = new List<ActivityBlock>();
            foreach (var b in blocks)
            {
                var (first, second) = Sessions.SplitBlock(b);
                insert.Add(first);
                if (second != null) insert.Add(second);
            }
            foreach (var b in blocks)
                _log.Blocks.Remove(b);
            _log.Blocks.InsertRange(Math.Min(idx, _log.Blocks.Count), insert);
            _log.Save();
            RefreshAll();
        }

        public void CreateShortcut(bool desktop)
        {
            try
            {
                var exe = Environment.ProcessPath ?? "";
                if (exe.Length == 0) { StatusText = "Cannot resolve exe path (single-file)"; return; }
                var dir = Path.GetDirectoryName(exe) ?? "";

                string lnk;
                if (desktop)
                    lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "SMENA.lnk");
                else
                {
                    var menuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "SMENA");
                    Directory.CreateDirectory(menuDir);
                    lnk = Path.Combine(menuDir, "SMENA.lnk");
                }

                string Esc(string s) => s.Replace("'", "''");
                var ps = "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('" + Esc(lnk) + "');" +
                         "$s.TargetPath='" + Esc(exe) + "';" +
                         "$s.WorkingDirectory='" + Esc(dir) + "';" +
                         "$s.IconLocation='" + Esc(exe) + ",0';" +
                         "$s.Description='SMENA time tracker';" +
                         "$s.Save()";
                var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -Command \"" + ps + "\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var p = System.Diagnostics.Process.Start(psi);
                p?.WaitForExit(6000);
                StatusText = "Shortcut created: " + lnk;
            }
            catch (Exception ex)
            {
                StatusText = "Shortcut failed: " + ex.Message;
            }
        }

        // ---- data safety (Settings → DATA) ----

        private string BackupDir => Path.Combine(DataFolder, "backups");

        public void BackupNow()
        {
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = Backups.SuggestName(DateTime.Now),
                    Filter = "SMENA backup (zip)|*.zip",
                    InitialDirectory = Directory.Exists(BackupDir) ? BackupDir : DataFolder,
                };
                if (dlg.ShowDialog() != true) return;
                _log.Save(); _store.Save(); _config.Save();   // current state flushes into the files first
                Backups.Create(dlg.FileName, DataFolder);
                StatusText = "Backup saved: " + dlg.FileName;
            }
            catch (Exception ex) { StatusText = "Backup failed: " + ex.Message; }
        }

        public void RestoreFromBackup()
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "SMENA backup (zip)|*.zip",
                    InitialDirectory = Directory.Exists(BackupDir) ? BackupDir : DataFolder,
                };
                if (dlg.ShowDialog() != true) return;

                var info = Backups.Inspect(dlg.FileName);
                var range = info.FirstBlock != null ? $" ({info.FirstBlock:dd.MM.yy} – {info.LastBlock:dd.MM.yy})" : "";
                var msg = $"Backup from {info.CreatedAt:dd.MM.yyyy HH:mm}:\n\n" +
                          $"{info.Projects} projects · {info.Tasks} tasks · {info.Blocks} blocks{range}\n\n" +
                          $"Replace current data? A safety copy of what is on disk now goes to:\n{BackupDir}";
                if (System.Windows.MessageBox.Show(msg, "SMENA — restore", System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes) return;

                _poller.Shutdown();   // live block + manual timer close cleanly before the files swap
                _log.Save(); _store.Save(); _config.Save();
                Directory.CreateDirectory(BackupDir);
                var safety = Path.Combine(BackupDir, $"SMENA_pre_restore_{DateTime.Now:yyyyMMdd_HHmm}.zip");
                Backups.Create(safety, DataFolder);
                Backups.Restore(dlg.FileName, DataFolder);

                _config.Load(); _store.Load(); _log.Reload();
                ReloadRules();
                ThemeApplier.Apply(_config.Current.Theme);
                RefreshAll();
                RefreshStatus(null);
                StatusText = $"Restored: {info.Blocks} blocks (safety: {Path.GetFileName(safety)})";
            }
            catch (Exception ex) { StatusText = "Restore failed: " + ex.Message; }
        }

        public void CleanupNow()
        {
            var months = _config.Current.CleanupMonths;
            if (months <= 0) { StatusText = "Cleanup is off — pick a retention first"; return; }
            try
            {
                var cutoff = DateTime.Now.AddMonths(-months);
                var victims = Backups.CountOlder(_log.Blocks, cutoff);
                if (victims == 0) { StatusText = $"Nothing older than {cutoff:dd.MM.yyyy}"; return; }
                var msg = $"Delete {victims} closed blocks older than {cutoff:dd.MM.yyyy}?\n\n" +
                          "The live block is not touched. A safety copy goes to backups first.";
                if (System.Windows.MessageBox.Show(msg, "SMENA — cleanup", System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes) return;

                _log.Save(); _store.Save(); _config.Save();
                Directory.CreateDirectory(BackupDir);
                var safety = Path.Combine(BackupDir, $"SMENA_pre_cleanup_{DateTime.Now:yyyyMMdd_HHmm}.zip");
                Backups.Create(safety, DataFolder);
                _log.Blocks.RemoveAll(b => !b.IsOpen && b.End < cutoff);
                _log.Save();
                RefreshAll();
                StatusText = $"Deleted {victims} blocks (safety: {Path.GetFileName(safety)})";
            }
            catch (Exception ex) { StatusText = "Cleanup failed: " + ex.Message; }
        }

        private void OnIdleReturned(TimeSpan gap, DateTime from, Guid? taskId)
        {
            var name = taskId == null ? "Unsorted" : (_store.Tasks.FirstOrDefault(t => t.Id == taskId)?.Name ?? "Unsorted");
            var msg = $"Вас не было {(int)gap.TotalMinutes} мин. Записать это время в «{name}»?";
            if (System.Windows.MessageBox.Show(msg, "SMENA", System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes)
            {
                _log.Add(new ActivityBlock
                {
                    Start = from,
                    End = from.Add(gap),
                    Process = "manual",
                    Title = "returned from idle",
                    TaskId = taskId
                });
                RefreshAll();
            }
        }

        public void ToggleManualFor(Guid? taskId)
        {
            if (_poller.ManualMode && _poller.ManualTaskId == taskId)
            {
                _poller.StopManual();
            }
            else if (taskId == null)
            {
                StartManualOn(null, "Unsorted", TimeSpan.Zero);
            }
            else
            {
                var t = _store.Tasks.FirstOrDefault(x => x.Id == taskId);
                if (t == null) return;
                StartManualOn(t, t.Name, TimeSpan.Zero);
            }
            OnPropertyChanged(nameof(IsManual));
            UpdateManualButton();
            RefreshStatus(null);
            RefreshAll();
        }

        /// <summary>Start the manual timer continuing from an existing amount of time ("HH:MM" already done). taskId null = Unsorted.</summary>
        public bool StartManualWithOffset(Guid? taskId, string offsetText)
        {
            TrackedTask? t = null;
            string name;
            if (taskId == null)
            {
                name = "Unsorted";
            }
            else
            {
                t = _store.Tasks.FirstOrDefault(x => x.Id == taskId);
                if (t == null) return false;
                name = t.Name;
            }
            if (!TimeSpan.TryParseExact((offsetText ?? "").Trim(), @"hh\:mm", null, out var ts) ||
                ts <= TimeSpan.Zero || ts > TimeSpan.FromHours(24))
            {
                StatusText = "Bad offset — use HH:MM, e.g. 03:30 (max 24h)";
                return false;
            }
            StartManualOn(t, name, ts);
            OnPropertyChanged(nameof(IsManual));
            UpdateManualButton();
            RefreshStatus(null);
            RefreshAll();
            return true;
        }

        private void StartManualOn(TrackedTask? t, string name, TimeSpan offset)
        {
            if (_poller.ManualMode) _poller.StopManual();
            _poller.StartManual(t?.Id, name, offset);
        }

        private void UpdateManualButton()
        {
            ManualButtonText = _poller.ManualMode
                ? $"STOP MANUAL ({FormatSpan(DateTime.Now - _poller.ManualStart)})"
                : "START MANUAL";
        }

        private void RefreshStatus(TrackStatus? s)
        {
            UpdateManualButton();
            if (_poller.IsPaused) { StatusText = "PAUSED — tracking off"; return; }
            if (_poller.ManualMode)
            {
                var tname = _poller.ManualTaskName;
                StatusText = $"● MANUAL — {tname}";
                return;
            }
            if (s != null && s.State == TrackState.Offline) { StatusText = "○ OFFLINE — system was asleep"; return; }
            if (s != null && s.State == TrackState.Locked) { StatusText = "○ LOCKED"; return; }
            if (s == null || s.State == TrackState.Idle) { StatusText = "○ IDLE"; return; }
            var task = string.IsNullOrEmpty(s.TaskName) ? "unsorted" : s.TaskName;
            StatusText = $"● {s.Process} → {task}";
        }

        // ---- data views ----

        public void RefreshAll()
        {
            RebuildDay();
            RebuildVisuals();
            RebuildApplications();
            RebuildTimerRows();
            RebuildWeek();
            Reports.Rebuild();
            Journal.Rebuild();
            DataChanged?.Invoke();
        }

        private void OnSecondTick()
        {
            if (_poller.ManualMode)
            {
                UpdateManualButton();
                StatusText = $"● MANUAL — {_poller.ManualTaskName} ({FormatClock(DateTime.Now - _poller.ManualStart)})";
            }
            if (_activeSessionStart != null && _rowMap.TryGetValue(_activeId ?? Guid.Empty, out var row))
                row.SessionText = FormatClock(DateTime.Now - _activeSessionStart.Value);

            UpdateHero();
            SecondTick?.Invoke();
        }

        private void UpdateHero()
        {
            var now = DateTime.Now;
            if (_poller.IsPaused)
            {
                HeroTime = "00:00:00";
                StatusLine = "paused — tracking off";
                IsRunning = false;
                return;
            }
            if (_poller.ManualMode)
            {
                HeroTime = FormatClock(now - _poller.ManualStart);
                StatusLine = $"manual — {_poller.ManualTaskName}";
            }
            else if (_activeSessionStart != null)
            {
                HeroTime = FormatClock(now - _activeSessionStart.Value);
                var s = _lastStatus;
                var task = s == null || string.IsNullOrEmpty(s.TaskName) ? "unsorted" : s.TaskName;
                StatusLine = s == null || s.State == TrackState.Idle || s.State == TrackState.Locked
                    ? (s != null && s.State == TrackState.Locked ? "locked" : "idle")
                    : $"{s.Process} — {task}";
            }
            else
            {
                HeroTime = "00:00:00";
                StatusLine = "idle";
            }
            IsRunning = _poller.ManualMode || _activeSessionStart != null;
        }

        private void RebuildTimerRows()
        {
            var ordered = _log.Blocks.OrderBy(b => b.Start).ToList();
            var closed = ordered.Where(b => !b.IsOpen).ToList();
            var todayStart = DateTime.Today;
            var todayEnd = todayStart.AddDays(1);

            double TodaySum(Guid? id) => closed
                .Where(b => b.TaskId == id && b.End > todayStart && b.Start < todayEnd)
                .Sum(b => Overlap(b, todayStart, todayEnd));
            double TotalSum(Guid? id) => closed
                .Where(b => b.TaskId == id)
                .Sum(b => (b.End - b.Start).TotalMinutes);

            var open = ordered.LastOrDefault(b => b.IsOpen);
            _activeId = _poller.ManualMode ? _poller.ManualTaskId : open?.TaskId;
            _activeSessionStart = _poller.ManualMode
                ? _poller.ManualStart
                : open == null ? null : Sessions.OpenSessionStart(ordered, _activeId);
            bool anyActive = _poller.ManualMode || open != null;

            var desired = new List<(Guid? key, string name, string proj)>();
            foreach (var p in _store.Projects)
                foreach (var t in _store.ActiveTasks().Where(t => t.ProjectId == p.Id))
                    desired.Add((t.Id, t.Name, p.Name));
            desired.Add((null, "Unsorted", ""));

            var alive = new HashSet<Guid?>();
            foreach (var d in desired)
            {
                var mapKey = d.key ?? Guid.Empty;   // Unsorted: Dictionary throws on a null key
                alive.Add(mapKey);                  // same key as _rowMap — or cleanup deletes the row
                if (!_rowMap.TryGetValue(mapKey, out var row))
                {
                    row = new TaskTimerRow { TaskId = d.key };
                    _rowMap[mapKey] = row;
                    TimerRows.Add(row);
                }
                row.Name = d.name;
                row.Project = d.proj;
                var trow = d.key == null ? null : _store.Tasks.FirstOrDefault(x => x.Id == d.key);
                row.Archived = trow?.ArchivedAt != null;
                row.TaskBrush = Palette.BrushFor(d.key, trow?.ColorHex);
                row.TodayText = FormatSpan(TimeSpan.FromMinutes(TodaySum(d.key)));
                row.TotalText = FormatSpan(TimeSpan.FromMinutes(TotalSum(d.key)));
                row.IsActive = anyActive && d.key == _activeId;   // null == null: Unsorted ticks too
                row.ManualMark = _poller.ManualMode && d.key == _activeId;
                row.Glyph = row.ManualMark ? "\uE71A" : "\uE768";
                row.SessionText = row.IsActive && _activeSessionStart != null
                    ? FormatClock(DateTime.Now - _activeSessionStart.Value)
                    : "";
            }

            foreach (var kv in _rowMap.Where(kv => !alive.Contains(kv.Key)).ToList())
            {
                TimerRows.Remove(kv.Value);
                _rowMap.Remove(kv.Key);
            }
        }

        private static string FormatClock(TimeSpan s) =>
            $"{(int)s.TotalHours:00}:{s.Minutes:00}:{s.Seconds:00}";

        private IEnumerable<ActivityBlock> RangeBlocks(DateTime from, DateTime to) =>
            _log.Blocks.Where(b => b.End > from && b.Start < to && !b.IsOpen);

        private void RebuildToday()
        {
            RebuildDay();
            RebuildApplications();
        }

        private void RebuildDay()
        {
            var day = SelectedDate;
            var blocks = RangeBlocks(day, day.AddDays(1))
                .Where(b => b.End > day)   // ignore blocks that merely touch midnight
                .OrderBy(b => b.Start).ToList();

            TodayRows.Clear();
            foreach (var g in MergeConsecutive(blocks))
            {
                TodayRows.Add(new BlockRow
                {
                    TimeRange = $"{g.Item2.Start:HH:mm}–{g.Item2.End:HH:mm}",
                    Duration = FormatSpan(g.Item2.End - g.Item2.Start),
                    App = g.Item2.Process,
                    Task = TaskName(g.Item2.TaskId),
                    Title = g.Item1,
                    Blocks = g.Item3
                });
            }

            // selected-day total + bars + stats
            var totalMin = blocks.Sum(b => (b.End - b.Start).TotalMinutes);
            StatTotal = FormatSpan(TimeSpan.FromMinutes(totalMin));

            TodayBars.Clear();
            var tree = Aggregation.BuildTree(blocks, _store.Projects, _store.Tasks, includeArchived: true);   // freshly closed tasks keep their today time visible
            var treeTotal = tree.Sum(n => n.Minutes);
            foreach (var node in tree)
            {
                TodayBars.Add(new TreeRow
                {
                    Name = node.Name + (node.IsArchived ? "  (closed)" : ""),
                    MinutesText = FormatSpan(TimeSpan.FromMinutes(node.Minutes)),
                    Percent = treeTotal < 1 ? 0 : Math.Min(100, node.Minutes / treeTotal * 100.0),
                    Bar = ProjectBrush(node),
                    Phase = node.Phase,
                    IsProject = node.IsProject,
                    IsUnsorted = node.IsUnsorted,
                    TaskId = node.TaskId,
                });
            }

            RecentRows.Clear();
            foreach (var r in TodayRows.Skip(Math.Max(0, TodayRows.Count - 5)))
                RecentRows.Add(r);

            StatLongest = blocks.Count == 0 ? "—" :
                FormatSpan(blocks.Max(b => b.End - b.Start));
            var weekBlocks = RangeBlocks(day.AddDays(-6), day.AddDays(1)).ToList();
            StatAvg = FormatSpan(TimeSpan.FromMinutes(weekBlocks.Sum(b => (b.End - b.Start).TotalMinutes) / 7.0));

            // header Today total is always the REAL today
            var realToday = DateTime.Today;
            var todayBlocks = RangeBlocks(realToday, realToday.AddDays(1));
            TodayTotal = FormatSpan(TimeSpan.FromMinutes(todayBlocks.Sum(b => (b.End - b.Start).TotalMinutes)));

            // current context chips (live open block)
            var open = _log.Blocks.LastOrDefault(b => b.IsOpen);
            CurrentApp = open?.Process ?? "";
            CurrentFile = open == null ? "" : ExtractFileName(open.Title);

            // re-resolve details selection against fresh rows (collapse/refresh can detach it)
            if (SelectedSession != null)
            {
                var b0 = SelectedSession.Blocks.FirstOrDefault();
                SelectedSession = b0 == null ? null : TodayRows.FirstOrDefault(r => r.Blocks.Contains(b0));
            }
        }

        private void RebuildApplications()
        {
            AppRows.Clear();
            var day = SelectedDate;
            var blocks = RangeBlocks(day, day.AddDays(1)).ToList();
            var total = blocks.Sum(b => (b.End - b.Start).TotalMinutes);
            foreach (var g in blocks.GroupBy(b => b.Process).OrderByDescending(g => g.Sum(b => (b.End - b.Start).TotalMinutes)))
            {
                var mins = g.Sum(b => (b.End - b.Start).TotalMinutes);
                AppRows.Add(new AppRow
                {
                    Name = g.Key,
                    DurationText = FormatSpan(TimeSpan.FromMinutes(mins)),
                    Percent = total < 1 ? 0 : Math.Min(100, mins / total * 100.0)
                });
            }
        }

        private static readonly System.Text.RegularExpressions.Regex FileRegex =
            new(@"([A-Za-z0-9_\- ]+\.(?:blend|ma|mb|hip|hipnc|hiplc|nk|spp|max|c4d|aep))\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        public static string ExtractFileName(string title)
        {
            var m = FileRegex.Match(title ?? "");
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        private void RebuildWeek()
        {
            var weekStart = DateTime.Today.AddDays(-6);
            var blocks = RangeBlocks(weekStart, DateTime.Today.AddDays(1)).ToList();

            WeekDayHeaders = Enumerable.Range(0, 7)
                .Select(i => $"{weekStart.AddDays(i):ddd} {weekStart.AddDays(i):dd.MM}")
                .ToArray();

            var desired = new List<(Guid? key, string name, string proj)>();
            // недельная сетка — отчёт: архивные таски с их историей остаются в таблице
            foreach (var p in _store.Projects)
                foreach (var t in _store.Tasks.Where(t => t.ProjectId == p.Id))
                    desired.Add((t.Id, t.Name, p.Name));
            desired.Add((null, "Unsorted", ""));

            WeekGrid.Clear();
            foreach (var r in Services.WeekGrid.Build(blocks, DateTime.Today, desired))
            {
                WeekGrid.Add(new WeekGridRow
                {
                    Name = r.Name,
                    Project = r.Project,
                    IsTotalRow = r.IsTotalRow,
                    IsGroupRow = r.IsGroupRow,
                    IsUnsorted = r.IsUnsorted,
                    Cells = r.Cells.Select(m => m < 1 ? "" : FormatSpan(TimeSpan.FromMinutes(m))).ToArray(),
                    Total = r.Total < 1 ? "" : FormatSpan(TimeSpan.FromMinutes(r.Total))
                });
            }
        }

        private static double Overlap(ActivityBlock b, DateTime from, DateTime to)
        {
            var end = b.End < to ? b.End : to;
            var start = b.Start > from ? b.Start : from;
            return Math.Max(0, (end - start).TotalMinutes);
        }

        private string TaskName(Guid? id)
        {
            if (id == null) return "Unsorted";
            return _store.Tasks.FirstOrDefault(t => t.Id == id)?.Name ?? "Deleted task";
        }

        public string TaskNameFor(Guid? id) => TaskName(id);

        /// <summary>Today's blocks including the open one — timeline strip source.</summary>
        public List<ActivityBlock> TodayBlocksForTimeline()
        {
            var today = DateTime.Today;
            return _log.Blocks
                .Where(b => b.End > today && b.Start < today.AddDays(1))
                .OrderBy(b => b.Start)
                .ToList();
        }

        // ---- assign drawer (шторка) ----

        private BlockRow? _assignRow;
        private List<BlockRow>? _assignRows;   // bulk mode: many selected rows

        public BlockRow? AssignRow
        {
            get => _assignRow;
            private set { _assignRow = value; OnPropertyChanged(nameof(AssignRow)); OnPropertyChanged(nameof(AssignOpen)); }
        }

        public bool AssignOpen => AssignRow != null;

        private string _assignSearch = "";
        public string AssignSearch
        {
            get => _assignSearch;
            set { _assignSearch = value; OnPropertyChanged(nameof(AssignSearch)); RebuildAssignTasks(); }
        }

        public ObservableCollection<TrackedTask> AssignTasks { get; } = new();

        public void BeginAssign(BlockRow row)
        {
            _assignRows = null;
            _mergeSourceId = null;
            _mergeUnsortedMode = false;
            MergeMode = false;
            AssignHeader = "ASSIGN TO TASK";
            OnPropertyChanged(nameof(AssignHeader));
            AssignRow = row;
            _assignSearch = "";
            OnPropertyChanged(nameof(AssignSearch));
            RebuildAssignTasks();
        }

        /// <summary>Bulk mode: assign every selected row (Ctrl/Shift multi-select in ACTIVITY LOG).</summary>
        public void BeginAssignBulk(System.Collections.IList rows)
        {
            var list = rows.Cast<BlockRow>().Where(r => r.Blocks.Count > 0).ToList();
            if (list.Count == 0) return;
            _assignRows = list;
            AssignRow = list[0];
            _mergeSourceId = null;
            _mergeUnsortedMode = false;
            MergeMode = false;
            AssignHeader = $"ASSIGN {list.Count} ROWS →";
            OnPropertyChanged(nameof(AssignHeader));
            _assignSearch = "";
            OnPropertyChanged(nameof(AssignSearch));
            RebuildAssignTasks();
        }

        public void CancelAssign()
        {
            _assignRows = null;
            _mergeSourceId = null;
            _mergeUnsortedMode = false;
            MergeMode = false;
            AssignHeader = "ASSIGN TO TASK";
            OnPropertyChanged(nameof(AssignHeader));
            AssignRow = null;
        }

        private void RebuildAssignTasks()
        {
            AssignTasks.Clear();
            var q = (_assignSearch ?? "").Trim();
            foreach (var p in _store.Projects)
                foreach (var t in _store.Tasks.Where(t => t.ProjectId == p.Id && (MergeMode == false || t.Id != _mergeSourceId)))
                    if (q.Length == 0 ||
                        t.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        p.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (t.Keywords ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                        AssignTasks.Add(t);
        }

        public void AssignTo(TrackedTask task)
        {
            if (MergeMode && _mergeUnsortedMode)
            {
                foreach (var b in _log.Blocks.Where(b => b.TaskId == null))
                {
                    b.TaskId = task.Id;
                    b.Assigned = "manual";
                }
                _log.Save();
                CancelAssign();
                RefreshAll();
                return;
            }
            if (MergeMode && _mergeSourceId != null)
            {
                var src = _mergeSourceId.Value;
                _mergeSourceId = null;
                MergeMode = false;
                AssignHeader = "ASSIGN TO TASK";
                OnPropertyChanged(nameof(AssignHeader));
                MergeTasks(src, task.Id);
                CancelAssign();
                return;
            }
            var rows = _assignRows ?? (AssignRow != null ? new List<BlockRow> { AssignRow } : null);
            if (rows == null) return;
            foreach (var r in rows)
                foreach (var b in r.Blocks)
                {
                    b.TaskId = task.Id;
                    b.Assigned = "manual";
                }
            _log.Save();
            CancelAssign();
            RefreshAll();
        }

        public TrackedTask? GetTask(Guid id) => _store.Tasks.FirstOrDefault(t => t.Id == id);

        // ---- merge mode: reassign ALL source-task time into a picked target task ----

        public bool MergeMode { get; private set; }
        private Guid? _mergeSourceId;

        private string _assignHeader = "ASSIGN TO TASK";
        public string AssignHeader { get => _assignHeader; set { _assignHeader = value; OnPropertyChanged(nameof(AssignHeader)); } }

        public void BeginMerge(Guid sourceId)
        {
            var src = GetTask(sourceId);
            if (src == null) return;
            _mergeSourceId = sourceId;
            MergeMode = true;
            _assignRows = null;
            AssignRow = new BlockRow { TimeRange = $"all time of '{src.Name}'", App = "merge", Title = src.Name, Blocks = new List<ActivityBlock>() };
            _assignSearch = "";
            AssignHeader = $"MERGE '{src.Name}' INTO →";
            OnPropertyChanged(nameof(AssignSearch));
            RebuildAssignTasks();
        }

        private bool _mergeUnsortedMode;

        /// <summary>Collapse: ALL Unsorted time (TaskId == null, all history) moves into a picked task.</summary>
        public void BeginMergeUnsorted()
        {            MergeMode = true;
            _mergeUnsortedMode = true;
            _mergeSourceId = null;
            _assignRows = null;
            AssignRow = new BlockRow { TimeRange = "all Unsorted time", App = "merge", Title = "Unsorted", Blocks = new List<ActivityBlock>() };
            _assignSearch = "";
            AssignHeader = "MERGE ALL UNSORTED INTO →";
            OnPropertyChanged(nameof(AssignHeader));
            RebuildAssignTasks();
        }

        public void RenameTask(Guid id, string name)
        {
            var t = GetTask(id);
            if (t == null || string.IsNullOrWhiteSpace(name)) return;
            t.Name = name.Trim();
            _store.Save();
            RefreshAll();
        }

        public void DeleteTaskNow(Guid id)
        {
            var t = GetTask(id);
            if (t == null) return;
            _store.Tasks.Remove(t);
            foreach (var b in _log.Blocks.Where(b => b.TaskId == id))
                b.TaskId = null;   // history survives as Unsorted, nothing is lost
            _store.Save();
            _log.Save();
            RefreshAll();
        }

        /// <summary>
        /// Reset the Unsorted bucket to zero: delete ALL closed unassigned blocks (all history).
        /// The live open block is left alone — the poller owns it; the assignment drawer is the
        /// tool for time worth keeping. Confirmation shows the exact amount being destroyed.
        /// </summary>
        public void ResetUnsortedToZero()
        {
            var junk = _log.Blocks.Where(b => b.TaskId == null && !b.IsOpen).ToList();
            if (junk.Count == 0) { StatusText = "Unsorted is already empty."; return; }
            var total = TimeSpan.FromMinutes(junk.Sum(b => (b.End - b.Start).TotalMinutes));
            var res = System.Windows.MessageBox.Show(
                $"Удалить всё время Unsorted?\n\nБлоков: {junk.Count} · всего {FormatSpan(total)}\nИсторию нельзя восстановить.",
                "SMENA", System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning);
            if (res != System.Windows.MessageBoxResult.OK) return;
            foreach (var b in junk)
                _log.Blocks.Remove(b);
            _log.Save();
            RefreshAll();
            StatusText = $"Unsorted reset: {junk.Count} blocks deleted.";
        }

        /// <summary>Collapse: all source-task time moves into the target task, source task is removed.</summary>
        public void MergeTasks(Guid sourceId, Guid targetId)
        {
            if (sourceId == targetId) return;
            var src = GetTask(sourceId);
            var dst = GetTask(targetId);
            if (src == null || dst == null) return;
            foreach (var b in _log.Blocks.Where(b => b.TaskId == sourceId))
            {
                b.TaskId = targetId;
                b.Assigned = "manual";
            }
            _store.Tasks.Remove(src);
            _log.Save();
            _store.Save();
            RefreshAll();
        }

        public TrackedTask? QuickAddTask(string name)
        {
            var n = (name ?? "").Trim();
            if (n.Length == 0) return null;
            var p = _store.Projects.FirstOrDefault();
            if (p == null)
            {
                p = new Project { Name = "General" };
                _store.Projects.Add(p);
            }
            var t = new TrackedTask { ProjectId = p.Id, Name = n };
            _store.Tasks.Add(t);
            _store.Save();
            ReloadProjects();
            RefreshAll();
            return t;
        }

        public void MoveTask(Guid taskId, Guid projectId)
        {
            var t = GetTask(taskId);
            if (t == null || t.ProjectId == projectId) return;
            if (!_store.Projects.Any(p => p.Id == projectId)) return;
            t.ProjectId = projectId;
            _store.Save();
            RebuildTaskList();
            RefreshAll();
        }

        public void SetTaskKeywords(Guid id, string keywords)
        {
            var t = GetTask(id);
            if (t == null) return;
            t.Keywords = keywords.Trim();
            _store.Save();
            RefreshAll();
        }

        public bool IsProjectArchived(Guid projectId) =>
            _store.Projects.FirstOrDefault(p => p.Id == projectId)?.ArchivedAt != null;

        /// <summary>Close a finished task: it leaves live views and the matcher, lands in the journal.</summary>
        public void CloseTask(Guid id) => SetTaskArchived(id, true);

        /// <summary>Return a journal task to the active set.</summary>
        public void ReopenTask(Guid id) => SetTaskArchived(id, false);

        public void SetTaskArchived(Guid id, bool archived)
        {
            var t = GetTask(id);
            if (t == null) return;
            t.ArchivedAt = archived ? DateTime.Now : null;
            _store.Save();
            RefreshAll();
            StatusText = archived ? $"Task closed → journal: {t.Name}" : $"Task reopened: {t.Name}";
        }

        public void SetProjectArchived(Guid projectId, bool archived)
        {
            var proj = _store.Projects.FirstOrDefault(p => p.Id == projectId);
            if (proj == null) return;
            proj.ArchivedAt = archived ? DateTime.Now : null;
            foreach (var t in _store.Tasks.Where(t => t.ProjectId == projectId))
                t.ArchivedAt = archived ? t.ArchivedAt ?? DateTime.Now : null;
            _store.Save();
            RefreshAll();
            StatusText = archived ? $"Project closed: {proj.Name}" : $"Project reopened: {proj.Name}";
        }

        public void SetTaskPhase(Guid id, string phase)
        {
            var t = GetTask(id);
            if (t == null) return;
            t.Phase = (phase ?? "").Trim();
            _store.Save();
            RefreshAll();
        }

        /// <summary>Set the accent color shown in stats; empty/null returns to the palette hash.</summary>
        public void SetTaskColor(Guid id, string? hex)
        {
            var t = GetTask(id);
            if (t == null) return;
            t.ColorHex = (hex ?? "").Trim();
            _store.Save();
            RefreshAll();
        }

        // ---- update check ----

        private bool _updateAvailable;
        public bool UpdateAvailable
        {
            get => _updateAvailable;
            set { _updateAvailable = value; OnPropertyChanged(nameof(UpdateAvailable)); }
        }

        private string _updateLabel = "";
        public string UpdateLabel
        {
            get => _updateLabel;
            set { _updateLabel = value; OnPropertyChanged(nameof(UpdateLabel)); }
        }

        private bool _updateDismissed;

        public void DismissUpdate() { _updateDismissed = true; UpdateAvailable = false; }

        public void OpenReleasesPage()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    UpdateChecker.ReleasesUrl) { UseShellExecute = true });
            }
            catch { StatusText = "Could not open the browser."; }
        }

        public string AppVersion => "v" + UpdateChecker.CurrentVersion().ToString();

        /// <summary>silent=true: no status text, used for the startup check.</summary>
        public async void CheckForUpdates(bool silent)
        {
            try
            {
                await Task.Delay(silent ? 8000 : 0);
                var tag = await UpdateChecker.FetchLatestTagAsync();
                if (tag == null) { if (!silent) StatusText = "Could not check for updates."; return; }
                if (UpdateChecker.IsNewer(tag, UpdateChecker.CurrentVersion()))
                {
                    if (_updateDismissed) return;
                    UpdateLabel = $"SMENA {tag} is available — click to download.";
                    UpdateAvailable = true;
                }
                else if (!silent)
                {
                    StatusText = $"SMENA is up to date ({AppVersion}).";
                }
            }
            catch
            {
                if (!silent) StatusText = "Could not check for updates.";
            }
        }

        // ---- theme ----

        public List<string> ThemeNames =>
            ThemeManager.Order.Select(k => ThemeManager.Themes[k].Name).ToList();

        public string ThemeName
        {
            get => ThemeManager.GetTheme(_config.Current.Theme).Name;
            set
            {
                var key = ThemeManager.KeyOfName(value);
                if (_config.Current.Theme == key) return;
                _config.Current.Theme = key;
                _config.Save();
                ThemeApplier.Apply(key);   // fires ThemeChanged → RefreshAll
                OnPropertyChanged(nameof(ThemeName));
            }
        }

        /// <summary>Persist the log after a timeline drag and refresh every view.</summary>
        public void SaveBlocks()
        {
            _log.Save();
            RefreshAll();
        }

        public BlockRow? FindRowForBlock(ActivityBlock block) =>
            TodayRows.FirstOrDefault(r => r.Blocks.Contains(block));

        // ---- block edit drawer (правка задним числом) ----

        private BlockRow? _editingRow;
        public BlockRow? EditingRow
        {
            get => _editingRow;
            private set { _editingRow = value; OnPropertyChanged(nameof(EditingRow)); OnPropertyChanged(nameof(EditBlockOpen)); }
        }

        public bool EditBlockOpen => EditingRow != null;

        private string _editBlockDate = "";
        public string EditBlockDate { get => _editBlockDate; set { _editBlockDate = value; OnPropertyChanged(nameof(EditBlockDate)); } }

        private string _editBlockStart = "";
        public string EditBlockStart { get => _editBlockStart; set { _editBlockStart = value; OnPropertyChanged(nameof(EditBlockStart)); } }

        private string _editBlockEnd = "";
        public string EditBlockEnd { get => _editBlockEnd; set { _editBlockEnd = value; OnPropertyChanged(nameof(EditBlockEnd)); } }

        private TrackedTask? _editBlockTask;
        public TrackedTask? EditBlockTask { get => _editBlockTask; set { _editBlockTask = value; OnPropertyChanged(nameof(EditBlockTask)); } }

        public ObservableCollection<TrackedTask> EditBlockTasks { get; } = new();

        public void BeginEditBlock(BlockRow row)
        {
            if (row.Blocks.Count == 0) return;
            EditingRow = row;
            EditBlockDate = row.Blocks[0].Start.ToString("dd.MM");
            EditBlockStart = row.Blocks[0].Start.ToString("HH:mm");
            EditBlockEnd = row.Blocks[^1].End.ToString("HH:mm");
            EditBlockTask = row.Blocks[0].TaskId == null
                ? null
                : _store.Tasks.FirstOrDefault(t => t.Id == row.Blocks[0].TaskId);
            ReloadEditBlockTasks();
        }

        private void ReloadEditBlockTasks()
        {
            EditBlockTasks.Clear();
            foreach (var p in _store.Projects)
                foreach (var t in _store.Tasks.Where(t => t.ProjectId == p.Id))
                    EditBlockTasks.Add(t);
        }

        public void SaveEditBlock()
        {
            if (EditingRow?.Blocks is not { Count: > 0 } blocks) return;
            var first = blocks[0];
            var last = blocks[^1];

            if (!TryParseDate(EditBlockDate, first.Start, out var day) ||
                !TryParseTime(EditBlockStart, out var startT) ||
                !TryParseTime(EditBlockEnd, out var endT))
            {
                StatusText = "Bad time format — use HH:MM and dd.MM";
                return;
            }

            var newStart = day + startT;
            var newEnd = day + endT;
            if (newEnd <= newStart) newEnd = newEnd.AddDays(1);   // crossed midnight
            if ((newEnd - newStart).TotalHours > 24)
            {
                StatusText = "Block longer than 24h";
                return;
            }

            var shiftStart = newStart - first.Start;
            var shiftEnd = newEnd - last.End;
            foreach (var b in blocks)
            {
                b.Start += shiftStart;
                b.End = ReferenceEquals(b, last) ? b.End + shiftEnd : b.End + shiftStart;
                b.TaskId = EditBlockTask?.Id;
                b.Assigned = "manual";
            }
            _log.Save();
            CloseEditBlock();
            RefreshAll();
        }

        public void DeleteEditingBlocks()
        {
            if (EditingRow?.Blocks == null) return;
            foreach (var b in EditingRow.Blocks)
                _log.Blocks.Remove(b);
            _log.Save();
            CloseEditBlock();
            RefreshAll();
        }

        public void CloseEditBlock() => EditingRow = null;

        /// <summary>Offline time: add a 1-hour block ending now, then open the editor on it.</summary>
        public void AddManualBlock()
        {
            var b = new ActivityBlock { Start = DateTime.Now.AddHours(-1), End = DateTime.Now, Process = "manual", Title = "added block" };
            _log.Add(b);
            RefreshAll();
            var row = FindRowForBlock(b) ?? new BlockRow
            {
                Blocks = new List<ActivityBlock> { b },
                TimeRange = $"{b.Start:HH:mm}–{b.End:HH:mm}",
                App = b.Process,
                Title = b.Title
            };
            BeginEditBlock(row);
        }

        /// <summary>
        /// Collapse selected rows (Shift-click range in ACTIVITY LOG) into ONE block
        /// (first start … last end), then open the assign drawer on it.
        /// </summary>
        public void CollapseSelected(System.Collections.IList rows)
        {
            var blocks = rows.Cast<BlockRow>()
                .SelectMany(r => r.Blocks)
                .OrderBy(b => b.Start)
                .ToList();
            if (blocks.Count == 0) return;

            var start = blocks[0].Start;
            var end = blocks[^1].End;
            var longest = blocks.OrderByDescending(b => (b.End - b.Start).TotalMinutes).First();
            Guid? taskId = blocks.Select(b => b.TaskId).Distinct().Count() == 1 ? blocks[0].TaskId : null;

            foreach (var b in blocks)
                _log.Blocks.Remove(b);
            var merged = new ActivityBlock { Start = start, End = end, Process = longest.Process, Title = longest.Title, TaskId = taskId, Assigned = "manual" };
            _log.Add(merged);
            RefreshAll();

            var row = FindRowForBlock(merged) ?? new BlockRow
            {
                Blocks = new List<ActivityBlock> { merged },
                TimeRange = $"{start:HH:mm}–{end:HH:mm}",
                App = merged.Process,
                Title = merged.Title ?? ""
            };
            BeginAssign(row);
        }

        private static bool TryParseDate(string s, DateTime reference, out DateTime d)
        {
            if (DateTime.TryParseExact(s.Trim(), "dd.MM", null, System.Globalization.DateTimeStyles.None, out d))
            {
                d = new DateTime(reference.Year, d.Month, d.Day);
                return true;
            }
            d = default;
            return false;
        }

        private static bool TryParseTime(string s, out TimeSpan t) =>
            TimeSpan.TryParseExact(s.Trim(), @"hh\:mm", null, out t);

        // ---- inline rename ----

        public void BeginRename(TaskTimerRow row)
        {
            row.EditName = row.Name;
            row.IsEditing = true;
        }

        public void CommitRename(TaskTimerRow row)
        {
            row.IsEditing = false;
            var name = (row.EditName ?? "").Trim();
            if (name.Length == 0 || name == row.Name) return;
            var t = _store.Tasks.FirstOrDefault(x => x.Id == row.TaskId);
            if (t == null) return;
            t.Name = name;
            _store.Save();
            row.Name = name;
        }

        /// <summary>Merge consecutive blocks with same (process, task); keep the longest title.</summary>
        private static List<(string, ActivityBlock, List<ActivityBlock>)> MergeConsecutive(List<ActivityBlock> ordered)
        {
            var result = new List<(string, ActivityBlock, List<ActivityBlock>)>();
            int i = 0;
            while (i < ordered.Count)
            {
                var first = ordered[i];
                var merged = new ActivityBlock { Start = first.Start, End = first.End, Process = first.Process, TaskId = first.TaskId, Title = first.Title };
                var underlying = new List<ActivityBlock> { first };
                int j = i + 1;
                while (j < ordered.Count &&
                       ordered[j].Process == merged.Process &&
                       ordered[j].TaskId == merged.TaskId &&
                       (ordered[j].Start - merged.End).TotalMinutes <= 1)
                {
                    merged.End = ordered[j].End;
                    if ((ordered[j].Title ?? "").Length > (merged.Title ?? "").Length)
                        merged.Title = ordered[j].Title;
                    underlying.Add(ordered[j]);
                    j++;
                }
                result.Add(((merged.Title ?? "").Trim(), merged, underlying));
                i = j;
            }
            return result;
        }

        private static string FormatSpan(TimeSpan s) =>
            s.TotalHours >= 1 ? $"{(int)s.TotalHours}h {s.Minutes:00}m" : $"{s.Minutes}m";

        private static System.Windows.Media.Brush ProjectBrush(AggNode node)
        {
            if (node.IsProject || node.IsUnsorted)
                return Palette.Frozen(string.IsNullOrEmpty(node.ColorHex) ? "#5AC8FA" : node.ColorHex);
            return Palette.Frozen(Palette.HexFor(node.TaskId, node.ColorHex));
        }

        // ---- projects / tasks CRUD ----

        private void ReloadProjects()
        {
            Projects.Clear();
            foreach (var p in _store.Projects) Projects.Add(p);
            SelectedProject = Projects.FirstOrDefault();
        }

        private void RebuildTaskList()
        {
            SelectedProjectTasks.Clear();
            if (SelectedProject == null) return;
            foreach (var t in _store.Tasks.Where(t => t.ProjectId == SelectedProject.Id))
                SelectedProjectTasks.Add(t);
            SelectedTask = SelectedProjectTasks.FirstOrDefault();
        }

        public void AddProject()
        {
            var name = NewProjectName?.Trim();
            if (string.IsNullOrEmpty(name)) return;
            var p = new Project { Name = name };
            _store.Projects.Add(p);
            _store.Save();
            NewProjectName = "";
            OnPropertyChanged(nameof(NewProjectName));
            Projects.Add(p);
            SelectedProject = p;
        }

        public void DeleteProject(Project? p)
        {
            if (p == null) return;
            if (System.Windows.MessageBox.Show($"Delete project '{p.Name}' and its tasks?", "SMENA",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes)
                return;
            foreach (var t in _store.Tasks.Where(t => t.ProjectId == p.Id).ToList())
                _store.Tasks.Remove(t);
            _store.Projects.Remove(p);
            _store.Save();
            ReloadProjects();
        }

        public void AddTask()
        {
            if (SelectedProject == null || string.IsNullOrWhiteSpace(NewTaskName)) return;
            var t = new TrackedTask { ProjectId = SelectedProject.Id, Name = NewTaskName.Trim(), Keywords = NewTaskKeywords?.Trim() ?? "" };
            _store.Tasks.Add(t);
            _store.Save();
            NewTaskName = ""; NewTaskKeywords = "";
            OnPropertyChanged(nameof(NewTaskName)); OnPropertyChanged(nameof(NewTaskKeywords));
            SelectedProjectTasks.Add(t);
            SelectedTask = t;
        }

        public void DeleteTask(TrackedTask? t)
        {
            if (t == null) return;
            _store.Tasks.Remove(t);
            _store.Save();
            SelectedProjectTasks.Remove(t);
            SelectedTask = SelectedProjectTasks.FirstOrDefault();
        }

        public void SaveTasks() => _store.Save();

        // ---- settings ----

        public int IdleTimeoutMinutes
        {
            get => _config.Current.IdleTimeoutMinutes;
            set { _config.Current.IdleTimeoutMinutes = Math.Max(1, value); _config.Save(); }
        }

        public bool AutoMode
        {
            get => _config.Current.AutoMode;
            set { _config.Current.AutoMode = value; _config.Save(); OnPropertyChanged(nameof(AutoModeLabel)); }
        }

        public bool IdleDetection
        {
            get => _config.Current.IdleDetection;
            set { _config.Current.IdleDetection = value; _config.Save(); }
        }

        public bool TrackFiles
        {
            get => _config.Current.TrackFiles;
            set { _config.Current.TrackFiles = value; _config.Save(); }
        }

        public string Exclusions
        {
            get => _config.Current.Exclusions;
            set { _config.Current.Exclusions = value ?? ""; _config.Save(); }
        }

        public string[] CleanupOptions { get; } =
            { "Keep everything", "Older than 3 months", "Older than 6 months", "Older than 1 year", "Older than 2 years" };
        private static readonly int[] CleanupMonthsMap = { 0, 3, 6, 12, 24 };

        public int CleanupMonthsIndex
        {
            get { var i = Array.IndexOf(CleanupMonthsMap, _config.Current.CleanupMonths); return i < 0 ? 0 : i; }
            set { _config.Current.CleanupMonths = CleanupMonthsMap[Math.Max(0, value)]; _config.Save(); }
        }

        public bool WidgetTopmost
        {
            get => _config.Current.WidgetTopmost;
            set { _config.Current.WidgetTopmost = value; _config.Save(); OnPropertyChanged(nameof(WidgetTopmost)); }
        }

        public double? WidgetLeft => _config.Current.WidgetLeft;
        public double? WidgetTop => _config.Current.WidgetTop;

        public void SaveWidgetPos(double left, double top)
        {
            _config.Current.WidgetLeft = left;
            _config.Current.WidgetTop = top;
            _config.Save();
        }

        public bool AutoStart
        {
            get => _config.Current.AutoStart;
            set
            {
                _config.Current.AutoStart = value;
                _config.Save();
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                    const string name = "SMENA";
                    if (value) key?.SetValue(name, $"\"{Environment.ProcessPath}\"");
                    else key?.DeleteValue(name, false);
                }
                catch { /* registry optional */ }
            }
        }

        public bool IdlePrompt
        {
            get => _config.Current.IdlePrompt;
            set { _config.Current.IdlePrompt = value; _config.Save(); }
        }

        // ---- rules ----

        public ObservableCollection<RuleRow> RuleRows { get; } = new();
        public IReadOnlyList<RuleTarget> RuleTargets { get; private set; } = Array.Empty<RuleTarget>();

        /// <summary>Sync the rules editor with the store (settings page load, restore).</summary>
        public void ReloadRules()
        {
            RuleRows.Clear();
            foreach (var r in _store.Rules)
                RuleRows.Add(new RuleRow(r, this));
            RuleTargets = _store.ActiveTasks()
                .Select(t => new RuleTarget(t.Id, $"{_store.ProjectName(t.ProjectId)} — {t.Name}"))
                .ToList();
            OnPropertyChanged(nameof(RuleTargets));
        }

        public string RuleTargetLabel(Guid taskId) =>
            _store.ActiveTasks().Where(t => t.Id == taskId)
                .Select(t => $"{_store.ProjectName(t.ProjectId)} — {t.Name}")
                .FirstOrDefault() ?? "(task gone)";

        public void SaveRules() => _store.Save();

        public void AddRule()
        {
            var rule = new Rule { TaskId = RuleTargets.FirstOrDefault()?.TaskId ?? Guid.Empty };
            _store.Rules.Add(rule);
            SaveRules();
            RuleRows.Add(new RuleRow(rule, this));
        }

        public void DeleteRule(RuleRow row)
        {
            _store.Rules.Remove(row.Rule);
            SaveRules();
            RuleRows.Remove(row);
        }

        public void MoveRule(RuleRow row, int delta)
        {
            int i = _store.Rules.IndexOf(row.Rule);
            int j = i + delta;
            if (i < 0 || j < 0 || j >= _store.Rules.Count) return;
            _store.Rules.RemoveAt(i);
            _store.Rules.Insert(j, row.Rule);
            SaveRules();
            RuleRows.RemoveAt(i);
            RuleRows.Insert(j, row);
        }

        /// <summary>Retro-apply rules to history: re-buckets auto-assigned, still-unsorted closed
        /// blocks by the current rules. Manual and open blocks are never touched.</summary>
        public void ApplyRulesToHistory()
        {
            try
            {
                var victims = _log.Blocks.Count(b => !b.IsOpen && b.Assigned == "auto" && b.TaskId == null);
                if (victims == 0) { StatusText = "No unsorted auto blocks to re-bucket"; return; }
                var msg = $"Re-bucket {victims} unsorted auto blocks by the current rules?\n\n" +
                          "Manual blocks and the live block are not touched.";
                if (System.Windows.MessageBox.Show(msg, "SMENA — rules", System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes) return;

                var n = Rules.Apply(_log.Blocks, _store.Rules,
                    id => _store.ActiveTasks().FirstOrDefault(t => t.Id == id));
                _log.Save();
                RefreshAll();
                StatusText = $"Rules re-bucketed {n} blocks";
            }
            catch (Exception ex) { StatusText = "Rules failed: " + ex.Message; }
        }

        // ---- search (Ctrl+K) ----

        private bool _searchOpen;
        public bool SearchOpen
        {
            get => _searchOpen;
            set { _searchOpen = value; OnPropertyChanged(nameof(SearchOpen)); }
        }

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set { _searchText = value ?? ""; OnPropertyChanged(nameof(SearchText)); RebuildSearch(); }
        }

        public System.Collections.ObjectModel.ObservableCollection<SearchHit> SearchHits { get; } = new();

        private void RebuildSearch()
        {
            SearchHits.Clear();
            foreach (var h in SMENA.Services.Search.Query(_log.Blocks, _store.ActiveTasks(), _store.Projects, _searchText))
                SearchHits.Add(h);
        }

        /// <summary>Jump to the hit's most recent day (Timeline) and close the overlay.</summary>
        public void Jump(SearchHit hit)
        {
            SearchOpen = false;
            if (hit == null) return;
            SelectedDate = hit.Day;
            NavigateRequested?.Invoke("Timeline");
        }

        // ---- export ----

        public bool ExportCsv(DateTime from, DateTime to)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                FileName = $"smena_{from:yyyyMMdd}_{to:yyyyMMdd}.csv"
            };
            if (dlg.ShowDialog() != true) return false;

            var names = new Dictionary<Guid, (string, string?)>();
            foreach (var t in _store.Tasks)
                names[t.Id] = (t.Name, _store.ProjectName(t.ProjectId));

            CsvExporter.Export(RangeBlocks(from, to.AddDays(1)), names, dlg.FileName);
            return true;
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));
    }
}
