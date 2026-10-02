using System;
using System.Collections.Generic;
using System.Linq;
using SMENA.Models;
using SMENA.Services;
using Xunit;

namespace SMENA.Tests
{
    public class WeekGridTests
    {
        [Fact]
        public void Build_SplitsBlocksByDay_AndAddsTotalRow()
        {
            var today = new DateTime(2026, 9, 30);       // Wednesday
            var taskId = Guid.NewGuid();
            var tasks = new List<(Guid?, string, string)> { (taskId, "tb3_01", "General") };
            var blocks = new List<ActivityBlock>
            {
                new() { Start = today.AddHours(10), End = today.AddHours(11.5), TaskId = taskId },                 // today, task
                new() { Start = today.AddDays(-1).AddHours(9), End = today.AddDays(-1).AddHours(10) },            // yesterday, unsorted
            };

            var rows = WeekGrid.Build(blocks, today, tasks);

            Assert.Equal(2, rows.Count);                    // task + TOTAL
            var taskRow = rows[0];
            Assert.Equal(90, taskRow.Cells[6], 0);          // today 1.5h
            Assert.Equal(0, taskRow.Cells[5], 0);           // yesterday — other task (unsorted)
            Assert.Equal(90, taskRow.Total, 0);
            var totalRow = rows[^1];
            Assert.True(totalRow.IsTotalRow);
            Assert.Equal(60, totalRow.Cells[5], 0);         // yesterday unsorted
            Assert.Equal(150, totalRow.Total, 0);
        }

        [Fact]
        public void Build_SplitsBlockCrossingMidnight()
        {
            var today = new DateTime(2026, 9, 30);
            var blocks = new List<ActivityBlock>
            {
                new() { Start = today.AddDays(-1).AddHours(23), End = today.AddHours(1) },   // 23:00 -> 01:00
            };

            var rows = WeekGrid.Build(blocks, today, new List<(Guid?, string, string)>());
            var totalRow = rows[^1];
            Assert.Equal(60, totalRow.Cells[5], 0);         // yesterday hour
            Assert.Equal(60, totalRow.Cells[6], 0);         // today hour
        }

        [Fact]
        public void Format_Adaquate() // sanity for cell formatting used by VM
        {
            var ts = TimeSpan.FromMinutes(125);
            Assert.Equal("2h 05m", $"{(int)ts.TotalHours}h {ts.Minutes:00}m");
        }

        [Fact]
        public void Build_GroupsTasksByProject_WithSubtotal()
        {
            var today = new DateTime(2026, 9, 30);
            var t1 = Guid.NewGuid(); var t2 = Guid.NewGuid(); var t3 = Guid.NewGuid();
            var tasks = new List<(Guid?, string, string)>
            {
                (t1, "tb3_rig", "General"),
                (t2, "tb3_mat", "General"),
                (t3, "other", "Rigging"),
            };
            var blocks = new List<ActivityBlock>
            {
                new() { Start = today.AddHours(9), End = today.AddHours(10), TaskId = t1 },
                new() { Start = today.AddHours(11), End = today.AddHours(12.5), TaskId = t2 },
                new() { Start = today.AddHours(13), End = today.AddHours(14), TaskId = t3 },
            };

            var rows = WeekGrid.Build(blocks, today, tasks);

            // General group + its 2 tasks, then "other" (single-task project: no group row), then TOTAL
            Assert.Equal(5, rows.Count);
            var group = rows[0];
            Assert.True(group.IsGroupRow);
            Assert.Equal("General", group.Name);
            Assert.Equal(150, group.Total, 0);              // 60 + 90 summed
            Assert.Equal(60, rows[1].Total, 0);
            Assert.Equal(90, rows[2].Total, 0);
            Assert.Equal(60, rows[3].Total, 0);             // single-task project: no subtotal row
            Assert.False(rows[3].IsGroupRow);
            Assert.Equal(210, rows[^1].Total, 0);
        }
    }
}
