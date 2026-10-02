using System;
using System.Windows.Media;

namespace SMENA.ViewModels
{
    /// <summary>One heatmap cell on the Reports CALENDAR card.</summary>
    public class CalendarDayVM
    {
        public DateTime Date { get; init; }
        public int Minutes { get; init; }
        public string DayNumber => Date.Day.ToString();
        public string MinutesText { get; init; } = "";
        public Brush Cell { get; init; } = Brushes.Transparent;
        public bool InMonth { get; init; }
        public bool IsToday { get; init; }
        public string Tooltip => $"{Date:dddd, dd.MM.yyyy} — {MinutesText}";
    }
}
