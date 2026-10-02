using System;
using System.Collections.Generic;
using System.Linq;
using SMENA.Models;

namespace SMENA.Services
{
    /// <summary>A row of the Project → Task aggregation tree (or a flat phase rollup row).</summary>
    public class AggNode
    {
        public string Name { get; set; } = "";
        public string ColorHex { get; set; } = "";
        public string Phase { get; set; } = "";
        public double Minutes { get; set; }
        public Guid? TaskId { get; set; }
        public Guid? ProjectId { get; set; }
        public bool IsProject { get; set; }
        public bool IsUnsorted { get; set; }
        public bool IsArchived { get; set; }
        public List<AggNode> Children { get; } = new();
    }

    /// <summary>
    /// Pure aggregation over time blocks — the only place minutes are computed.
    /// Time is never stored: projects/tasks always roll up from their blocks.
    /// </summary>
    public static class Aggregation
    {
        private static double MinutesOf(ActivityBlock b) => (b.End - b.Start).TotalMinutes;

        private static bool IsArchived(TrackedTask? t) => t != null && t.ArchivedAt != null;

        /// <summary>Build a Project → Task tree from range-filtered blocks.
        /// Unsorted time becomes a single trailing node. Archived items are excluded
        /// from live views (includeArchived: true — reports keep them).</summary>
        public static List<AggNode> BuildTree(
            IEnumerable<ActivityBlock> blocks,
            IReadOnlyCollection<Project> projects,
            IReadOnlyCollection<TrackedTask> tasks,
            bool includeArchived = false)
        {
            var projById = projects.ToDictionary(p => p.Id);
            var taskById = tasks.ToDictionary(t => t.Id);

            var projNodes = new Dictionary<Guid, AggNode>();
            var taskNodes = new Dictionary<Guid, AggNode>();
            var unsorted = new AggNode { Name = "Unsorted", IsUnsorted = true, ColorHex = "#5A5A64" };

            foreach (var b in blocks)
            {
                TrackedTask? task = b.TaskId != null && taskById.TryGetValue(b.TaskId.Value, out var t) ? t : null;
                if (task == null)
                {
                    unsorted.Minutes += MinutesOf(b);
                    continue;
                }
                projById.TryGetValue(task.ProjectId, out var proj);
                bool archived = IsArchived(task) || (proj != null && proj.ArchivedAt != null);
                if (archived && !includeArchived) continue;

                if (!projNodes.TryGetValue(task.ProjectId, out var pNode))
                {
                    pNode = new AggNode
                    {
                        Name = proj?.Name ?? "Unsorted",
                        ProjectId = task.ProjectId,
                        IsProject = true,
                        ColorHex = proj?.ColorHex ?? "",
                        IsArchived = proj != null && proj.ArchivedAt != null,
                    };
                    projNodes[task.ProjectId] = pNode;
                }

                if (!taskNodes.TryGetValue(task.Id, out var tNode))
                {
                    tNode = new AggNode
                    {
                        Name = task.Name,
                        TaskId = task.Id,
                        ProjectId = task.ProjectId,
                        Phase = task.Phase,
                        ColorHex = string.IsNullOrEmpty(task.ColorHex) ? pNode.ColorHex : task.ColorHex,
                        IsArchived = IsArchived(task),
                    };
                    taskNodes[task.Id] = tNode;
                    pNode.Children.Add(tNode);
                }
                tNode.Minutes += MinutesOf(b);
                pNode.Minutes += MinutesOf(b);
            }

            var result = projNodes.Values
                .Where(pn => pn.Minutes > 0 || pn.Children.Count > 0)
                .OrderByDescending(pn => pn.Minutes)
                .ToList();
            if (unsorted.Minutes > 0) result.Add(unsorted);
            foreach (var pn in result) Sort(pn.Children);
            return result;
        }

        private static void Sort(List<AggNode> children)
        {
            var sorted = children.OrderByDescending(c => c.Minutes).ToList();
            children.Clear();
            foreach (var c in sorted) children.Add(c);
        }

        /// <summary>Flat phase rollup (tasks with a Phase tag), minutes descending.</summary>
        public static List<AggNode> PhaseRollup(
            IEnumerable<ActivityBlock> blocks,
            IReadOnlyCollection<TrackedTask> tasks,
            bool includeArchived = false)
        {
            var taskById = tasks.ToDictionary(t => t.Id);
            var byPhase = new Dictionary<string, AggNode>(StringComparer.OrdinalIgnoreCase);

            foreach (var b in blocks)
            {
                if (b.TaskId == null || !taskById.TryGetValue(b.TaskId.Value, out var task)) continue;
                if (string.IsNullOrWhiteSpace(task.Phase)) continue;
                if (!includeArchived && IsArchived(task)) continue;

                var key = task.Phase.Trim();
                if (!byPhase.TryGetValue(key, out var node))
                {
                    byPhase[key] = node = new AggNode { Name = key, Phase = key };
                }
                node.Minutes += MinutesOf(b);
            }

            return byPhase.Values.OrderByDescending(n => n.Minutes).ToList();
        }

        /// <summary>Flat export rows (project, task, phase, minutes) for the given range blocks.</summary>
        public static List<(string Project, string Task, string Phase, double Minutes)> FlatRows(
            IEnumerable<ActivityBlock> blocks,
            IReadOnlyCollection<Project> projects,
            IReadOnlyCollection<TrackedTask> tasks)
        {
            var projById = projects.ToDictionary(p => p.Id);
            var taskById = tasks.ToDictionary(t => t.Id);
            var rows = new List<(string, string, string, double)>();

            foreach (var g in blocks.Where(b => b.TaskId != null && taskById.ContainsKey(b.TaskId.Value))
                                     .GroupBy(b => b.TaskId!.Value))
            {
                var task = taskById[g.Key];
                var proj = projById.TryGetValue(task.ProjectId, out var p) ? p.Name : "Unsorted";
                rows.Add((proj, task.Name, task.Phase, g.Sum(MinutesOf)));
            }
            var unsorted = blocks.Where(b => b.TaskId == null || !taskById.ContainsKey(b.TaskId.Value)).Sum(MinutesOf);
            if (unsorted > 0) rows.Add(("Unsorted", "Unsorted", "", unsorted));
            return rows.OrderByDescending(r => r.Item4).ToList();
        }
    }
}
