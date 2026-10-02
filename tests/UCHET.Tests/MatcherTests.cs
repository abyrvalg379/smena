using System;
using System.Collections.Generic;
using System.IO;
using UCHET.Models;
using UCHET.Services;
using Xunit;

namespace UCHET.Tests
{
    public class MatcherTests
    {
        private static Matcher Make() => new Matcher(() => new List<TrackedTask>
        {
            new TrackedTask { Name = "TB-3 rig", Keywords = "tb3" },
            new TrackedTask { Name = "Polosatik", Keywords = "polosatik, rigging" },
            new TrackedTask { Name = "Maya general", Keywords = "maya" },
        });

        [Fact]
        public void Match_KeywordInTitle_BeatsProcessKeyword()
        {
            var m = Make();
            // "tb3" hits the title (tier 0); process "maya" hits "Maya general" (tier 1) — title wins.
            Assert.Equal("TB-3 rig", m.Match("maya", "scene_tb3_final_v012.ma - main scene")?.Name);
        }

        [Fact]
        public void Match_IsCaseInsensitive()
        {
            var m = Make();
            Assert.Equal("Polosatik", m.Match("blender", "POLOSATIK_rig_v2.blend - Blender")?.Name);
        }

        [Fact]
        public void Match_ProcessNameEqualsKeyword()
        {
            var m = Make();
            Assert.Equal("Maya general", m.Match("maya", "untitled")?.Name);
        }

        [Fact]
        public void Match_LongestKeywordWins()
        {
            var m = new Matcher(() => new List<TrackedTask>
            {
                new TrackedTask { Name = "Short", Keywords = "tb3" },
                new TrackedTask { Name = "Specific", Keywords = "tb3_rig" },
            });
            Assert.Equal("Specific", m.Match("maya", "tb3_rig_v012.ma")?.Name);
        }

        [Fact]
        public void Match_NoKeywordHit_ReturnsNull()
        {
            var m = Make();
            Assert.Null(m.Match("chrome", "YouTube - some video"));
        }

        [Fact]
        public void KeywordList_SplitsTrimsAndSkipsEmpty()
        {
            var t = new TrackedTask { Keywords = " tb3 , rigging ,, " };
            Assert.Equal(new[] { "tb3", "rigging" }, t.KeywordList);
        }

        [Fact]
        public void KeywordList_EmptyFallsBackToTaskName()
        {
            var t = new TrackedTask { Name = "tb3_01", Keywords = "" };
            Assert.Equal(new[] { "tb3_01" }, t.KeywordList);
            var m = new Matcher(() => new List<TrackedTask> { t });
            Assert.Equal("tb3_01", m.Match("Adobe Substance 3D Painter", "Adobe Substance 3D Painter - tb3_01")?.Name);
        }
    }

    public class TimeLogTests
    {
        [Fact]
        public void RoundTrip_PreservesBlocks_AndClosesOpenOnes()
        {
            var dir = Path.Combine(Path.GetTempPath(), "uchet_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var log = new TimeLog(dir);
                log.Add(new ActivityBlock
                {
                    Start = new DateTime(2026, 9, 30, 10, 0, 0),
                    End = new DateTime(2026, 9, 30, 11, 30, 0),
                    Process = "maya",
                    Title = "tb3.ma",
                    TaskId = Guid.NewGuid()
                });
                log.Add(new ActivityBlock
                {
                    Start = new DateTime(2026, 9, 30, 12, 0, 0),
                    End = new DateTime(2026, 9, 30, 12, 5, 0),
                    Process = "blender",
                    Title = "wip.blend",
                    IsOpen = true
                });

                var reloaded = new TimeLog(dir);
                Assert.Equal(2, reloaded.Blocks.Count);
                Assert.True(reloaded.Blocks[1].IsOpen, "open block survives load — recovery prompt decides");
                reloaded.CloseOpenBlocks();
                Assert.False(reloaded.Blocks[1].IsOpen);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
