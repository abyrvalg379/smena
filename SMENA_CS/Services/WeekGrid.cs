using System;
using System.Collections.Generic;
using System.Linq;
using SMENA.Models;

namespace SMENA.Services
{
    public class WeekGridData
    {
        public string Name { get; init; } = "";
        public string Project { get; init; } = "";
        public double[] Cells { get; init; } = new double[7];   // minutes per day, oldest -> today
        public double Total { get; init; }
        public bool IsTotalRow { get; init; }
        public bool IsGroupRow { get; init; }                   // project subtotal (sum of its tasks)
        public bool IsUnsorted { get; init; }
    }

    /// <summary>
    /// Week grid: tasks x days (7 days ending today), project subtotal rows for
    /// multi-task projects, plus a per-day totals row.
    /// </summary>
    public static class WeekGrid
    {
        public static List<WeekGridData> Build(IEnumerable<ActivityBlock> blocksInWeek, DateTime today,
            IEnumerable<(Guid? Id, string Name, string Project)> orderedTasks)
        {
            var result = new List<WeekGridData>();
            var weekStart = today.AddDays(-6);

            double Sum(Guid? id, int day)
            {
                var from = weekStart.AddDays(day);
                var to = from.AddDays(1);
                double m = 0;
                foreach (var b in blocksInWeek)
                    if (b.TaskId == id && b.End > from && b.Start < to && !b.IsOpen)
                    {
                        var end = b.End < to ? b.End : to;          // Math.Min/Max have no DateTime overloads
                        var start = b.Start > from ? b.Start : from;
                        m += Math.Max(0, (end - start).TotalMinutes);
                    }
                return m;
            }

            var projects = new List<string>();
            foreach (var t in orderedTasks)
                if (!projects.Contains(t.Project))
                    projects.Add(t.Project);

            foreach (var project in projects)
            {
                var members = orderedTasks.Where(t => t.Project == project).ToList();
                if (members.Count == 0) continue;

                if (members.Count > 1)
                {
                    var cells = new double[7];
                    double total = 0;
                    for (int i = 0; i < 7; i++)
                        foreach (var m in members)
                            cells[i] += Sum(m.Id, i);
                    for (int i = 0; i < 7; i++) total += cells[i];
                    result.Add(new WeekGridData { Name = project, Project = project, Cells = cells, Total = total, IsGroupRow = true });
                }

                foreach (var m in members)
                {
                    var cells = new double[7];
                    double total = 0;
                    for (int i = 0; i < 7; i++) { cells[i] = Sum(m.Id, i); total += cells[i]; }
                    result.Add(new WeekGridData { Name = m.Name, Project = m.Project, Cells = cells, Total = total, IsUnsorted = m.Id == null });
                }
            }

            // Totals straight from blocks — time of deleted tasks must not vanish.
            var totals = new double[7];
            double weekTotal = 0;
            for (int i = 0; i < 7; i++)
            {
                var from = weekStart.AddDays(i);
                var to = from.AddDays(1);
                foreach (var b in blocksInWeek)
                {
                    if (b.IsOpen || b.End <= from || b.Start >= to) continue;
                    var end = b.End < to ? b.End : to;
                    var start = b.Start > from ? b.Start : from;
                    var m = Math.Max(0, (end - start).TotalMinutes);
                    totals[i] += m;
                    weekTotal += m;
                }
            }
            result.Add(new WeekGridData { Name = "TOTAL", Cells = totals, Total = weekTotal, IsTotalRow = true });

            return result;
        }
    }
}
