using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Threading;
using UCHET.Models;

namespace UCHET.Services
{
    public enum TrackState { Recording, Idle, Manual, Paused }

    public class TrackStatus
    {
        public TrackState State { get; init; }
        public string Process { get; init; } = "";
        public string Title { get; init; } = "";
        public string TaskName { get; init; } = "";
        public string ProjectName { get; init; } = "";
        public DateTime ManualStart { get; init; }
    }

    /// <summary>
    /// Foreground-window poller. Keeps one open ActivityBlock per contiguous
    /// (process, title, task) run; closes it on switch or idle (End = last input).
    /// While a manual timer runs, auto capture is paused (no double counting).
    /// </summary>
    public class Poller : IDisposable
    {
        private readonly TimeLog _log;
        private readonly Matcher _matcher;
        private readonly ConfigManager _config;
        private readonly TaskStore _store;
        private readonly DispatcherTimer _tick;
        private readonly DispatcherTimer _flush;
        private readonly Dictionary<int, string> _procCache = new();

        private ActivityBlock? _open;

        public bool ManualMode { get; private set; }
        public DateTime ManualStart { get; private set; }
        public Guid? ManualTaskId { get; private set; }
        public string ManualTaskName { get; private set; } = "";
        public bool IsPaused { get; private set; }

        /// <summary>Global pause: nothing is logged until resumed. Manual timer counts as explicit activity — it unpauses.</summary>
        public void SetPaused(bool paused)
        {
            if (IsPaused == paused) return;
            IsPaused = paused;
            if (paused)
            {
                if (ManualMode) { ManualMode = false; ManualTaskId = null; ManualTaskName = ""; }
                CloseOpen(DateTime.Now);
                _log.Save();
            }
            Emit(IsPaused ? TrackState.Paused : TrackState.Recording);
        }

        public event Action<TrackStatus>? StatusChanged;

        /// <summary>User returned from an idle episode — (gap, idle-start, task that was active before idle).</summary>
        public event Action<TimeSpan, DateTime, Guid?>? IdleReturned;

        private bool _idleEpisode;
        private DateTime _idleEnded;
        private Guid? _idleTaskId;

        public Poller(TimeLog log, Matcher matcher, ConfigManager config, TaskStore store)
        {
            _log = log;
            _matcher = matcher;
            _config = config;
            _store = store;

            _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _tick.Tick += (_, _) => Tick();
            _tick.Start();

            _flush = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _flush.Tick += (_, _) => { if (_open != null) _log.Save(); };
            _flush.Start();
        }

        public void StartManual(Guid? taskId, string taskName, TimeSpan initial = default)
        {
            IsPaused = false;   // explicit manual start overrides pause
            CloseOpen(DateTime.Now);
            ManualMode = true;
            ManualStart = DateTime.Now - initial;   // resume-style start: timer continues from `initial`
            ManualTaskId = taskId;                  // null = Unsorted (untracked bucket)
            ManualTaskName = taskName;
            Emit(TrackState.Manual);
        }

        public void StopManual()
        {
            if (!ManualMode) return;
            var end = DateTime.Now;
            if (end > ManualStart)
            {
                _log.Add(new ActivityBlock
                {
                    Start = ManualStart,
                    End = end,
                    Process = "manual",
                    Title = ManualTaskName,
                    TaskId = ManualTaskId
                });
            }
            ManualMode = false;
            ManualTaskId = null;
            ManualTaskName = "";
            Emit(TrackState.Recording);
        }

        public void Tick()
        {
            if (ManualMode) return;
            if (IsPaused || !_config.Current.AutoMode)
            {
                Emit(TrackState.Paused);
                return;
            }

            var now = DateTime.Now;
            if (_config.Current.IdleDetection)
            {
                var lastInput = Win32.GetLastInputTime();
                if ((now - lastInput).TotalMinutes >= _config.Current.IdleTimeoutMinutes)
                {
                    _idleEpisode = true;
                    _idleEnded = lastInput;
                    _idleTaskId = _open?.TaskId;
                    if (_open != null)
                    {
                        CloseOpen(lastInput);
                        _log.Save();
                    }
                    Emit(TrackState.Idle);
                    return;
                }
            }

            if (_idleEpisode)
            {
                // returned from idle: first active tick after an episode
                _idleEpisode = false;
                if (_config.Current.IdlePrompt)
                {
                    var gap = now - _idleEnded;
                    if (gap.TotalMinutes >= 1)
                        IdleReturned?.Invoke(gap, _idleEnded, _idleTaskId);
                }
            }

            var hwnd = Win32.GetForegroundWindow();
            if (hwnd == IntPtr.Zero || !Win32.IsWindowVisible(hwnd))
            {
                if (_open != null) { CloseOpen(now); _log.Save(); }
                Emit(TrackState.Idle);
                return;
            }

            string title = Win32.GetWindowTitle(hwnd);
            Win32.GetWindowThreadProcessId(hwnd, out uint pid);
            string process = GetProcessName((int)pid);
            var task = _matcher.Match(process, title);

            if (_open != null &&
                _open.Process == process &&
                _open.Title == title &&
                _open.TaskId == task?.Id)
            {
                _open.End = now;
                Emit(TrackState.Recording, process, title, task);
                return;
            }

            CloseOpen(now);
            _open = new ActivityBlock { Start = now, End = now, Process = process, Title = title, TaskId = task?.Id, IsOpen = true };
            _log.Add(_open);
            Emit(TrackState.Recording, process, title, task);
        }

        /// <summary>Close the open block and persist everything (app exit). A running manual timer is saved as a block.</summary>
        public void Shutdown()
        {
            if (ManualMode)
            {
                var end = DateTime.Now;
                if (end > ManualStart)
                {
                    _log.Add(new ActivityBlock
                    {
                        Start = ManualStart,
                        End = end,
                        Process = "manual",
                        Title = ManualTaskName,
                        TaskId = ManualTaskId
                    });
                }
                ManualMode = false;
                ManualTaskId = null;
                ManualTaskName = "";
            }
            CloseOpen(DateTime.Now);
            _log.Save();
        }

        private void CloseOpen(DateTime end)
        {
            if (_open == null) return;
            _open.End = end;
            _open.IsOpen = false;
            if (_open.End <= _open.Start)
                _log.Blocks.Remove(_open);
            _open = null;
        }

        private string GetProcessName(int pid)
        {
            if (_procCache.TryGetValue(pid, out var name)) return name;
            try { name = Process.GetProcessById(pid).ProcessName; }
            catch { name = "unknown"; }
            _procCache[pid] = name;
            return name;
        }

        private void Emit(TrackState state, string process = "", string title = "", TrackedTask? task = null)
        {
            StatusChanged?.Invoke(new TrackStatus
            {
                State = state,
                Process = process,
                Title = title,
                TaskName = task?.Name ?? "",
                ProjectName = task == null ? "" : _store.ProjectName(task.ProjectId) ?? "",
                ManualStart = ManualStart
            });
        }

        public void Dispose()
        {
            _tick.Stop();
            _flush.Stop();
        }
    }
}
