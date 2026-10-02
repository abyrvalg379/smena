using System;
using System.Collections.Generic;
using System.Linq;
using SMENA.Models;

namespace SMENA.Services
{
    /// <summary>One row of the Ctrl+K search results. Day is the jump target.</summary>
    public class SearchHit
    {
        public string Kind { get; set; } = "";      // TASK / PROJECT / BLOCK
        public string Title { get; set; } = "";
        public string Sub { get; set; } = "";
        public DateTime Day { get; set; }
    }

    /// <summary>History search (Ctrl+K): case-insensitive substring match across tasks,
    /// projects and blocks. Every whitespace-separated token must match (AND) within a
    /// hit's combined fields (task: name + keywords + phase + project; block: title +
    /// process + note). Tasks and projects sort first, then blocks newest-first; Day
    /// points at the most recent activity of the hit.</summary>
    public static class Search
    {
        public static List<SearchHit> Query(
            IEnumerable<ActivityBlock> blocks,
            IEnumerable<TrackedTask> tasks,
            IEnumerable<Project> projects,
            string query, int limit = 30)
        {
            var tokens = (query ?? "")
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => t.ToLowerInvariant())
                .ToArray();
            if (tokens.Length == 0) return new List<SearchHit>();

            bool Matches(string haystack) => haystack != null && tokens.All(haystack.ToLowerInvariant().Contains);
            var today = DateTime.Today;

            var projectList = projects.ToList();
            var projectById = projectList.ToDictionary(p => p.Id);
            var taskList = tasks.ToList();
            var taskById = taskList.ToDictionary(t => t.Id);

            var hits = new List<SearchHit>();

            foreach (var p in projectList.Where(p => Matches(p.Name)).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                var day = blocks.Where(b => b.TaskId != null
                                            && taskById.TryGetValue(b.TaskId.Value, out var bt)
                                            && bt.ProjectId == p.Id)
                                .Select(b => b.Start.Date)
                                .DefaultIfEmpty(today)
                                .Max();
                hits.Add(new SearchHit { Kind = "PROJECT", Title = p.Name, Sub = LastSeen(day), Day = day });
            }

            foreach (var t in taskList.Where(t => Matches(
                        t.Name + "\n" + t.Keywords + "\n" + t.Phase + "\n" +
                        (projectById.TryGetValue(t.ProjectId, out var tp) ? tp.Name : "")))
                                   .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                var day = blocks.Where(b => b.TaskId == t.Id).Select(b => b.Start.Date)
                                .DefaultIfEmpty(today).Max();
                projectById.TryGetValue(t.ProjectId, out var proj);
                hits.Add(new SearchHit { Kind = "TASK", Title = t.Name, Sub = proj?.Name ?? "", Day = day });
            }

            hits.AddRange(blocks
                .Where(b => Matches(b.Title + "\n" + b.Process + "\n" + b.Note))
                .OrderByDescending(b => b.Start)
                .Take(limit)
                .Select(b => new SearchHit
                {
                    Kind = "BLOCK",
                    Title = string.IsNullOrWhiteSpace(b.Title) ? b.Process : b.Title,
                    Sub = $"{b.Start:dd.MM.yy HH:mm} · {b.Process}",
                    Day = b.Start.Date,
                }));

            return hits;
        }

        private static string LastSeen(DateTime day) =>
            day == DateTime.Today ? "today" : $"last {day:dd.MM.yy}";
    }
}
