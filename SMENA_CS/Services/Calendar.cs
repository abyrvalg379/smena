using System;
using System.Collections.Generic;
using System.Linq;
using SMENA.Models;

namespace SMENA.Services
{
    public record CalendarDay(DateTime Date, int Minutes, bool InMonth);

    public record CalendarMonth(
        IReadOnlyList<CalendarDay> Days,
        int TotalMinutes,
        int ActiveDays,
        int BestDayMinutes,
        DateTime? BestDayDate);

    /// <summary>
    /// Month heatmap math: Monday-first grid covering the whole month (extra cells
    /// belong to neighbour months), block time split across midnight, totals over
    /// in-month days only. Pure — unit tested.
    /// </summary>
    public static class Calendar
    {
        public static CalendarMonth Build(int year, int month, IEnumerable<ActivityBlock> blocks)
        {
            var minutes = MinutesByDay(blocks);

            var first = new DateTime(year, month, 1);
            var last = new DateTime(year, month, DateTime.DaysInMonth(year, month));
            // Monday-first grid: pad the 1st back to Monday, the last day forward to Sunday
            var gridStart = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));
            var gridEnd = last.AddDays((7 - (((int)last.DayOfWeek + 6) % 7) - 1) % 7);

            var days = new List<CalendarDay>();
            int total = 0, active = 0, best = 0;
            DateTime? bestDate = null;
            for (var d = gridStart; d <= gridEnd; d = d.AddDays(1))
            {
                bool inMonth = d.Month == month;
                minutes.TryGetValue(d, out var m);
                days.Add(new CalendarDay(d, m, inMonth));
                if (!inMonth) continue;
                total += m;
                if (m > 0) active++;
                if (m > best) { best = m; bestDate = d; }
            }
            return new CalendarMonth(days, total, active, best, bestDate);
        }

        /// <summary>Closed blocks attributed to days; a block crossing midnight is split.</summary>
        public static Dictionary<DateTime, int> MinutesByDay(IEnumerable<ActivityBlock> blocks)
        {
            var map = new Dictionary<DateTime, int>();
            foreach (var b in blocks)
            {
                if (b.IsOpen || b.End <= b.Start) continue;
                var day = b.Start.Date;
                while (day <= b.End.Date)
                {
                    var segStart = b.Start > day ? b.Start : day;
                    var next = day.AddDays(1);
                    var segEnd = b.End < next ? b.End : next;
                    if (segEnd > segStart)
                    {
                        var mins = (int)(segEnd - segStart).TotalMinutes;
                        map[day] = map.TryGetValue(day, out var m) ? m + mins : mins;
                    }
                    day = next;
                }
            }
            return map;
        }
    }
}
