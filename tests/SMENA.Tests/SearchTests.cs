using System;
using System.Collections.Generic;
using System.Linq;
using SMENA.Models;
using SMENA.Services;
using Xunit;

namespace SMENA.Tests
{
    public class SearchTests
    {
        private static readonly Project Admin = new() { Id = Guid.NewGuid(), Name = "ADMIN" };
        private static readonly Project Rig = new() { Id = Guid.NewGuid(), Name = "Rigs" };

        private static readonly TrackedTask Tb3 = new() { Id = Guid.NewGuid(), ProjectId = Rig.Id, Name = "TB-3 rig", Keywords = "tb3, bomber", Phase = "modeling" };
        private static readonly TrackedTask Docs = new() { Id = Guid.NewGuid(), ProjectId = Admin.Id, Name = "Docs reading" };

        private static List<ActivityBlock> Blocks() => new()
        {
            new ActivityBlock { Start = new DateTime(2026, 9, 30, 10, 0, 0), End = new DateTime(2026, 9, 30, 11, 0, 0), Process = "chrome", Title = "github — smena repo" },
            new ActivityBlock { Start = new DateTime(2026, 10, 1, 12, 0, 0), End = new DateTime(2026, 10, 1, 13, 0, 0), Process = "maya", Title = "tb3_wing_v012.ma", TaskId = Tb3.Id },
            new ActivityBlock { Start = new DateTime(2026, 10, 2, 9, 0, 0), End = new DateTime(2026, 10, 2, 9, 30, 0), Process = "notepad", Title = "todo tb3", Note = "check wing shape", TaskId = Tb3.Id },
        };

        private static SearchHit? One(List<SearchHit> hits, string kind, string title) =>
            hits.FirstOrDefault(h => h.Kind == kind && h.Title.Contains(title));

        [Fact]
        public void Query_Empty_Or_Whitespace_Returns_Nothing()
        {
            Assert.Empty(Search.Query(Blocks(), new[] { Tb3 }, new[] { Admin, Rig }, ""));
            Assert.Empty(Search.Query(Blocks(), new[] { Tb3 }, new[] { Admin, Rig }, "   "));
            Assert.Empty(Search.Query(Blocks(), new[] { Tb3 }, new[] { Admin, Rig }, null));
        }

        [Fact]
        public void Query_Tokens_Must_All_Match()
        {
            var hits = Search.Query(Blocks(), new[] { Tb3 }, new[] { Rig }, "tb3 maya");
            Assert.Contains(hits, h => h.Kind == "BLOCK" && h.Title.Contains("tb3_wing"));
            Assert.DoesNotContain(hits, h => h.Kind == "BLOCK" && h.Title.Contains("todo"));
        }

        [Fact]
        public void Query_Finds_Task_By_Name_Keyword_And_Phase()
        {
            var hits = Search.Query(Blocks(), new[] { Tb3, Docs }, new[] { Rig }, "bomber");
            Assert.NotNull(One(hits, "TASK", "TB-3 rig"));
            hits = Search.Query(Blocks(), new[] { Tb3, Docs }, new[] { Rig }, "modeling");
            Assert.NotNull(One(hits, "TASK", "TB-3 rig"));
            hits = Search.Query(Blocks(), new[] { Tb3, Docs }, new[] { Rig }, "docs");
            Assert.NotNull(One(hits, "TASK", "Docs"));
        }

        [Fact]
        public void Query_Finds_Task_By_Project_Name()
        {
            var hits = Search.Query(Blocks(), new[] { Tb3 }, new[] { Rig }, "rigs");
            Assert.NotNull(One(hits, "TASK", "TB-3 rig"));
            Assert.NotNull(One(hits, "PROJECT", "Rigs"));
        }

        [Fact]
        public void Query_Finds_Block_By_Title_Process_And_Note()
        {
            Assert.NotNull(One(Search.Query(Blocks(), new[] { Tb3 }, new[] { Rig }, "github"), "BLOCK", "github"));
            Assert.NotNull(One(Search.Query(Blocks(), new[] { Tb3 }, new[] { Rig }, "notepad"), "BLOCK", "todo"));
            Assert.NotNull(One(Search.Query(Blocks(), new[] { Tb3 }, new[] { Rig }, "wing shape"), "BLOCK", "todo"));
        }

        [Fact]
        public void Query_Is_Case_Insensitive()
        {
            Assert.NotNull(One(Search.Query(Blocks(), new[] { Tb3 }, new[] { Rig }, "TB3_WING"), "BLOCK", "tb3_wing"));
        }

        [Fact]
        public void Query_Tasks_And_Projects_Come_Before_Blocks()
        {
            var hits = Search.Query(Blocks(), new[] { Tb3 }, new[] { Rig }, "tb3");
            Assert.Equal("TASK", hits[0].Kind);
            Assert.All(hits.TakeWhile(h => h.Kind != "BLOCK"), h => Assert.NotEqual("BLOCK", h.Kind));
            Assert.Contains(hits, h => h.Kind == "BLOCK");
        }

        [Fact]
        public void Query_Blocks_Newest_First_And_Limited()
        {
            var many = Enumerable.Range(0, 50).Select(i => new ActivityBlock
            {
                Start = new DateTime(2026, 9, 1).AddDays(i),
                End = new DateTime(2026, 9, 1).AddDays(i).AddHours(1),
                Process = "maya", Title = "blockhit",
            }).ToList();
            var hits = Search.Query(many, Array.Empty<TrackedTask>(), Array.Empty<Project>(), "blockhit", limit: 10);
            Assert.Equal(10, hits.Count);
            Assert.Equal(new DateTime(2026, 10, 20), hits[0].Day);   // newest first
        }

        [Fact]
        public void Query_Hit_Day_Is_Last_Activity()
        {
            var hits = Search.Query(Blocks(), new[] { Tb3 }, new[] { Rig }, "tb3");
            var task = One(hits, "TASK", "TB-3 rig");
            Assert.Equal(new DateTime(2026, 10, 2), task!.Day);      // last tb3 block's day
            var projHits = Search.Query(Blocks(), new[] { Tb3 }, new[] { Rig }, "rigs");
            var proj = One(projHits, "PROJECT", "Rigs");
            Assert.Equal(new DateTime(2026, 10, 2), proj!.Day);      // day = last activity of project's tasks
            var noBlocks = Search.Query(Blocks(), new[] { Docs }, new[] { Admin }, "docs");
            Assert.Equal(DateTime.Today, One(noBlocks, "TASK", "Docs")!.Day);  // no blocks → today
        }
    }
}
