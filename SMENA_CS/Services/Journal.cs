using System;
using System.Collections.Generic;
using System.Linq;
using SMENA.Models;

namespace SMENA.Services
{
    public record JournalEntry(
        Guid TaskId, string Name, string ProjectName, string Phase,
        DateTime? ClosedAt, int Minutes);

    /// <summary>
    /// Closed-task journal: Task.ArchivedAt doubles as the close date. The journal
    /// lists closed tasks with their all-time tracked minutes, newest closed first.
    /// Pure — unit tested.
    /// </summary>
    public static class Journal
    {
        public static List<JournalEntry> Entries(
            IEnumerable<ActivityBlock> blocks, IEnumerable<Project> projects, IEnumerable<TrackedTask> tasks)
        {
            var projName = projects.ToDictionary(p => p.Id, p => p.Name);
            var minutes = blocks
                .Where(b => !b.IsOpen && b.TaskId != null)
                .GroupBy(b => b.TaskId!.Value)
                .ToDictionary(g => g.Key, g => (int)g.Sum(b => (b.End - b.Start).TotalMinutes));

            return tasks
                .Where(t => t.ArchivedAt != null)
                .OrderByDescending(t => t.ArchivedAt)
                .Select(t => new JournalEntry(
                    t.Id,
                    t.Name,
                    t.ProjectId is Guid pid && projName.TryGetValue(pid, out var pn) ? pn : "",
                    t.Phase ?? "",
                    t.ArchivedAt,
                    minutes.TryGetValue(t.Id, out var m) ? m : 0))
                .ToList();
        }
    }
}
