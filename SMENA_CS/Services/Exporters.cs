using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SMENA.Models;

namespace SMENA.Services
{
    public static class Exporters
    {
        public static void WriteCsv(string path, IReadOnlyList<ActivityBlock> blocks, Func<Guid?, (string Task, string? Project)> names)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Date,Project,Task,Minutes,Hours,Blocks");
            foreach (var g in blocks.GroupBy(b => new { Date = b.Start.Date, b.TaskId }).OrderBy(g => g.Key.Date))
            {
                var (task, project) = names(g.Key.TaskId);
                var minutes = g.Sum(b => (b.End - b.Start).TotalMinutes);
                sb.Append(g.Key.Date.ToString("yyyy-MM-dd")).Append(',')
                  .Append(Escape(project ?? "")).Append(',')
                  .Append(Escape(task)).Append(',')
                  .Append(minutes.ToString("0", CultureInfo.InvariantCulture)).Append(',')
                  .Append((minutes / 60.0).ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                  .Append(g.Count()).AppendLine();
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        public static void WriteJson(string path, IReadOnlyList<ActivityBlock> blocks, Func<Guid?, (string Task, string? Project)> names)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[");
            var items = blocks.OrderBy(b => b.Start).ToList();
            for (int i = 0; i < items.Count; i++)
            {
                var b = items[i];
                var (task, project) = names(b.TaskId);
                sb.Append("  { \"start\": \"").Append(b.Start.ToString("yyyy-MM-dd HH:mm"))
                  .Append("\", \"end\": \"").Append(b.End.ToString("yyyy-MM-dd HH:mm"))
                  .Append("\", \"minutes\": ").Append(((int)(b.End - b.Start).TotalMinutes).ToString(CultureInfo.InvariantCulture))
                  .Append(", \"project\": \"").Append(Json(project ?? ""))
                  .Append("\", \"task\": \"").Append(Json(task))
                  .Append("\", \"app\": \"").Append(Json(b.Process))
                  .Append("\", \"file\": \"").Append(Json(b.Title))
                  .Append("\", \"source\": \"").Append(Json(b.EffectiveSource))
                  .Append("\", \"note\": \"").Append(Json(b.Note ?? ""))
                  .Append("\" }");
                if (i < items.Count - 1) sb.Append(',');
                sb.AppendLine();
            }
            sb.AppendLine("]");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        public static void WriteMarkdown(string path, string title, IReadOnlyList<ActivityBlock> blocks, Func<Guid?, (string Task, string? Project)> names)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# ").Append(title).AppendLine().AppendLine();
            var total = blocks.Sum(b => (b.End - b.Start).TotalMinutes);
            sb.AppendLine("Total: ").Append(Fmt(total)).AppendLine().AppendLine();
            sb.AppendLine("## Projects").AppendLine();
            foreach (var g in blocks.GroupBy(b => b.TaskId).OrderByDescending(g => g.Sum(b => (b.End - b.Start).TotalMinutes)))
            {
                var (task, project) = names(g.Key);
                var mins = g.Sum(b => (b.End - b.Start).TotalMinutes);
                sb.Append("- ").Append(string.IsNullOrEmpty(project) ? task : project + " / " + task)
                  .Append(" — ").AppendLine(Fmt(mins));
            }
            sb.AppendLine().AppendLine("## Sessions").AppendLine();
            foreach (var b in blocks.OrderBy(b => b.Start))
            {
                var (task, project) = names(b.TaskId);
                sb.Append(b.Start.ToString("HH:mm")).Append("–").Append(b.End.ToString("HH:mm"))
                  .Append(" — ").Append(string.IsNullOrEmpty(project) ? task : project + " / " + task).AppendLine();
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        public static void WriteHtml(string path, string title, IReadOnlyList<ActivityBlock> blocks, Func<Guid?, (string Task, string? Project)> names)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>").Append(EscapeH(title))
              .AppendLine("</title><style>body{background:#0B0F14;color:#E8EDF3;font-family:Segoe UI,sans-serif;padding:32px}" +
                          "table{border-collapse:collapse;width:100%}td,th{border:1px solid #263241;padding:6px 10px;font-size:13px}" +
                          "th{background:#151D27;text-align:left}h1{font-size:20px}</style></head><body>");
            sb.Append("<h1>").Append(EscapeH(title)).AppendLine("</h1><table><tr><th>Start</th><th>End</th><th>Duration</th><th>Project</th><th>Task</th><th>App</th><th>Note</th></tr>");
            foreach (var b in blocks.OrderBy(b => b.Start))
            {
                var (task, project) = names(b.TaskId);
                sb.Append("<tr><td>").Append(b.Start.ToString("HH:mm"))
                  .Append("</td><td>").Append(b.End.ToString("HH:mm"))
                  .Append("</td><td>").Append(Fmt((b.End - b.Start).TotalMinutes))
                  .Append("</td><td>").Append(EscapeH(project ?? ""))
                  .Append("</td><td>").Append(EscapeH(task))
                  .Append("</td><td>").Append(EscapeH(b.Process))
                  .Append("</td><td>").Append(EscapeH(b.Note ?? ""))
                  .AppendLine("</td></tr>");
            }
            sb.AppendLine("</table></body></html>");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static string Fmt(double minutes) =>
            minutes >= 60 ? $"{(int)(minutes / 60)}h {Math.Round(minutes % 60):00}m" : $"{Math.Round(minutes):0}m";

        private static string Escape(string s) =>
            (s ?? "").Contains(',') || (s ?? "").Contains('"') || (s ?? "").Contains('\n')
                ? "\"" + s.Replace("\"", "\"\"") + "\""
                : s ?? "";

        private static string Json(string s) =>
            (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "");

        private static string EscapeH(string s) =>
            (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
