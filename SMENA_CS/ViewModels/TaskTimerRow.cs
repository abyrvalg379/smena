using System;
using System.ComponentModel;

namespace SMENA.ViewModels
{
    /// <summary>One row of the live stopwatch board: task ▸ session ▸ today ▸ total.</summary>
    public class TaskTimerRow : INotifyPropertyChanged
    {
        public Guid? TaskId { get; init; }

        private string _name = "";
        public string Name { get => _name; set { _name = value; OnPropertyChanged(nameof(Name)); } }

        private string _project = "";
        public string Project { get => _project; set { _project = value; OnPropertyChanged(nameof(Project)); } }

        private string _sessionText = "";
        public string SessionText { get => _sessionText; set { _sessionText = value; OnPropertyChanged(nameof(SessionText)); } }

        private string _todayText = "";
        public string TodayText { get => _todayText; set { _todayText = value; OnPropertyChanged(nameof(TodayText)); } }

        private string _totalText = "";
        public string TotalText { get => _totalText; set { _totalText = value; OnPropertyChanged(nameof(TotalText)); } }

        private bool _isActive;
        public bool IsActive { get => _isActive; set { _isActive = value; OnPropertyChanged(nameof(IsActive)); } }

        private bool _manualMark;
        public bool ManualMark { get => _manualMark; set { _manualMark = value; OnPropertyChanged(nameof(ManualMark)); } }

        private string _glyph = "\uE768";
        public string Glyph { get => _glyph; set { _glyph = value; OnPropertyChanged(nameof(Glyph)); } }

        private System.Windows.Media.Brush _taskBrush = System.Windows.Media.Brushes.Gray;
        public System.Windows.Media.Brush TaskBrush { get => _taskBrush; set { _taskBrush = value; OnPropertyChanged(nameof(TaskBrush)); } }

        private bool _isEditing;
        public bool IsEditing { get => _isEditing; set { _isEditing = value; OnPropertyChanged(nameof(IsEditing)); } }

        private string _editName = "";
        public string EditName { get => _editName; set { _editName = value; OnPropertyChanged(nameof(EditName)); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
