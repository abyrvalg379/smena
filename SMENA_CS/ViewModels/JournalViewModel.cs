using System;
using System.Collections.ObjectModel;
using System.Linq;
using SMENA.Models;
using SMENA.Services;

namespace SMENA.ViewModels
{
    public class JournalRow
    {
        public Guid TaskId { get; init; }
        public string Name { get; init; } = "";
        public string ProjectName { get; init; } = "";
        public string ClosedText { get; init; } = "";
        public string TotalText { get; init; } = "";
        public System.Windows.Media.Brush Dot { get; init; } = System.Windows.Media.Brushes.Gray;
    }

    /// <summary>Closed-task journal page data. Reopen/delete go through MainViewModel.</summary>
    public class JournalViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        private readonly TaskStore _store;
        private readonly TimeLog _log;

        public ObservableCollection<JournalRow> Rows { get; } = new();

        private string _summary = "";
        public string Summary { get => _summary; private set { _summary = value; OnPropertyChanged(nameof(Summary)); } }

        public JournalViewModel(TaskStore store, TimeLog log)
        {
            _store = store;
            _log = log;
        }

        public void Rebuild()
        {
            var entries = Journal.Entries(_log.Blocks, _store.Projects, _store.Tasks);
            Rows.Clear();
            foreach (var e in entries)
            {
                var t = _store.Tasks.FirstOrDefault(x => x.Id == e.TaskId);
                Rows.Add(new JournalRow
                {
                    TaskId = e.TaskId,
                    Name = e.Name,
                    ProjectName = e.ProjectName,
                    ClosedText = e.ClosedAt?.ToString("dd.MM.yyyy") ?? "",
                    TotalText = FormatSpan(TimeSpan.FromMinutes(e.Minutes)),
                    Dot = Palette.BrushFor(e.TaskId, t?.ColorHex),
                });
            }
            var total = entries.Sum(e => e.Minutes);
            Summary = entries.Count == 0
                ? "No closed tasks yet — close one from the dashboard (✓ on a task row)"
                : $"{entries.Count} closed · {FormatSpan(TimeSpan.FromMinutes(total))} tracked all-time";
        }

        private static string FormatSpan(TimeSpan s) =>
            s.TotalHours >= 1 ? $"{(int)s.TotalHours}h {s.Minutes:00}m" : $"{s.Minutes}m";

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));
    }
}
