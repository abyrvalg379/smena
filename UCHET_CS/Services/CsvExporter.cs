using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UCHET.Models;

namespace UCHET.Services
{
    /// <summary>CSV export: one row per (date, task), UTF-8 BOM so Excel opens it cleanly.</summary>
    public static class CsvExporter
    {
        public static void Export(IEnumerable<ActivityBlock> blocks, IDictionary<Guid, (string Task, string? Project)> names, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Date,Project,Task,Minutes,Hours,Blocks");

            foreach (var g in blocks
                .GroupBy(b => new { Date = b.Start.Date, b.TaskId })
                .OrderBy(g => g.Key.Date))
            {
                var (task, project) = g.Key.TaskId != null && names.TryGetValue(g.Key.TaskId.Value, out var n) ? n : ("Unsorted", "");
                var minutes = g.Sum(b => (b.End - b.Start).TotalMinutes);
                sb.Append(g.Key.Date.ToString("yyyy-MM-dd")).Append(',')
                  .Append(Escape(project)).Append(',')
                  .Append(Escape(task)).Append(',')
                  .Append(minutes.ToString("0")).Append(',')
                  .Append((minutes / 60.0).ToString("0.00")).Append(',')
                  .Append(g.Count()).AppendLine();
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static string Escape(string? s)
        {
            s ??= "";
            return s.Contains(',') || s.Contains('"') || s.Contains('\n')
                ? "\"" + s.Replace("\"", "\"\"") + "\""
                : s;
        }
    }
}
