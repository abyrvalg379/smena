using System.ComponentModel;

namespace UCHET.ViewModels
{
    /// <summary>One row of the WEEK grid: task x 7 days + total.</summary>
    public class WeekGridRow : INotifyPropertyChanged
    {
        public string Name { get; set; } = "";
        public string Project { get; set; } = "";
        public string[] Cells { get; set; } = System.Array.Empty<string>();
        public string Total { get; set; } = "";
        public bool IsTotalRow { get; set; }
        public bool IsGroupRow { get; set; }
        public bool IsUnsorted { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
