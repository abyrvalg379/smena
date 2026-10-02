using System;
using System.Collections.Generic;
using SMENA.Models;
using SMENA.Services;
using Xunit;

namespace SMENA.Tests
{
    public class JournalTests
    {
        private static Project Proj(Guid id, string name) => new() { Id = id, Name = name };

        // TrackedTask alias kept short in fixtures
        private static TrackedTask T(Guid id, Guid projId, string name, DateTime? closed) =>
            new() { Id = id, ProjectId = projId, Name = name, ArchivedAt = closed };

        [Fact]
        public void Entries_Closed_Only_With_Minutes_And_Project()
        {
            var p = Guid.NewGuid();
            var t1 = Guid.NewGuid();
            var t2 = Guid.NewGuid();
            var blocks = new List<ActivityBlock>
            {
                new() { Start = DateTime.Today.AddHours(9), End = DateTime.Today.AddHours(11), TaskId = t1 },
                new() { Start = DateTime.Today.AddHours(12), End = DateTime.Today.AddHours(12).AddMinutes(30), TaskId = t1 },
                new() { Start = DateTime.Today.AddHours(8), End = DateTime.Today.AddHours(9), TaskId = t2, IsOpen = true },
            };
            var tasks = new List<TrackedTask>
            {
                T(t1, p, "Hull", DateTime.Today.AddDays(-1)),
                T(t2, p, "Turret", null),   // active — not in the journal
            };
            var entries = Journal.Entries(blocks, new[] { Proj(p, "TB-3") }, tasks);

            var e = Assert.Single(entries);
            Assert.Equal("Hull", e.Name);
            Assert.Equal("TB-3", e.ProjectName);
            Assert.Equal(150, e.Minutes);
            Assert.Equal(DateTime.Today.AddDays(-1), e.ClosedAt);
        }

        [Fact]
        public void Entries_Newest_Closed_First()
        {
            var p = Guid.NewGuid();
            var a = Guid.NewGuid(); var b = Guid.NewGuid();
            var tasks = new List<TrackedTask>
            {
                T(a, p, "older", new DateTime(2026, 9, 1)),
                T(b, p, "newer", new DateTime(2026, 10, 1)),
            };
            var entries = Journal.Entries(Array.Empty<ActivityBlock>(), new[] { Proj(p, "P") }, tasks);
            Assert.Equal(2, entries.Count);
            Assert.Equal("newer", entries[0].Name);
            Assert.Equal("older", entries[1].Name);
            Assert.Equal(0, entries[0].Minutes);
        }
    }
}
