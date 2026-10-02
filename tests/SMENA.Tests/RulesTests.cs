using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SMENA.Models;
using SMENA.Services;
using Xunit;

namespace SMENA.Tests
{
    public class RulesTests
    {
        private static readonly TrackedTask Rig = new() { Id = Guid.NewGuid(), ProjectId = Guid.NewGuid(), Name = "TB-3 rig" };
        private static readonly TrackedTask Admin = new() { Id = Guid.NewGuid(), ProjectId = Guid.NewGuid(), Name = "ADMIN bucket" };

        private static Func<Guid, TrackedTask?> Map(params TrackedTask[] tasks)
        {
            var byId = tasks.ToDictionary(t => t.Id, t => t);
            return id => byId.TryGetValue(id, out var t) ? t : null;
        }

        // ---- IsMatch ----

        [Theory]
        [InlineData("maya", "", "maya.exe", "anything", true)]     // process-only rule
        [InlineData("", "scene_tb3", "blender", "scene_tb3_v012", true)] // title-only rule
        [InlineData("maya", "tb3", "maya", "idle scene", false)]   // AND: both must hit
        [InlineData("MAYA", "", "maya", "x", true)]                // case-insensitive
        [InlineData("^maya$", "", "maya", "x", true)]              // anchors work
        [InlineData("^maya$", "", "maya.exe", "x", false)]         // partial vs anchored
        public void IsMatch_Regex_Semantics(string procRe, string titleRe, string proc, string title, bool expected)
            => Assert.Equal(expected, Rules.IsMatch(procRe, titleRe, proc, title));

        [Fact]
        public void IsMatch_Invalid_Regex_Is_Inert_Not_Fatal()
        {
            Assert.False(Rules.IsMatch("[unclosed", "tb3", "maya", "scene_tb3.ma"));
        }

        // ---- Match ----

        [Fact]
        public void Match_First_Rule_In_List_Order_Wins()
        {
            var rules = new List<Rule>
            {
                new() { ProcessRegex = "maya", TaskId = Admin.Id },
                new() { ProcessRegex = "maya", TaskId = Rig.Id },
            };
            Assert.Equal("ADMIN bucket", Rules.Match(rules, Map(Rig, Admin), "maya", "scene_tb3.ma")!.Name);
        }

        [Fact]
        public void Match_Disabled_And_Dead_Rules_Skipped()
        {
            var rules = new List<Rule>
            {
                new() { Enabled = false, ProcessRegex = "maya", TaskId = Rig.Id },
                new() { ProcessRegex = "maya", TaskId = Guid.NewGuid() },   // deleted target
                new() { TitleRegex = "tb3", TaskId = Admin.Id },            // valid fallback
            };
            Assert.Equal("ADMIN bucket", Rules.Match(rules, Map(Rig, Admin), "maya", "scene_tb3.ma")!.Name);
        }

        [Fact]
        public void Match_No_Hit_Returns_Null()
        {
            var rules = new List<Rule> { new() { TitleRegex = "nomatch", TaskId = Rig.Id } };
            Assert.Null(Rules.Match(rules, Map(Rig), "explorer", "Проводник"));
        }

        // ---- Apply (history re-bucketing) ----

        [Fact]
        public void Apply_ReBuckets_Only_Auto_Unsorted_Closed_Blocks()
        {
            var rules = new List<Rule> { new() { ProcessRegex = "maya", TaskId = Rig.Id } };
            var target = new ActivityBlock { Start = DateTime.Now, End = DateTime.Now, Process = "maya", Title = "junk scene", Assigned = "auto" };
            var manual = new ActivityBlock { Start = DateTime.Now, End = DateTime.Now, Process = "maya", Assigned = "manual" };
            var assigned = new ActivityBlock { Start = DateTime.Now, End = DateTime.Now, Process = "maya", TaskId = Admin.Id, Assigned = "auto" };
            var open = new ActivityBlock { Start = DateTime.Now, End = DateTime.Now, Process = "maya", IsOpen = true, Assigned = "auto" };
            var untouched = new ActivityBlock { Start = DateTime.Now, End = DateTime.Now, Process = "notepad", Assigned = "auto" };
            var blocks = new List<ActivityBlock> { target, manual, assigned, open, untouched };

            var n = Rules.Apply(blocks, rules, Map(Rig, Admin));

            Assert.Equal(1, n);
            Assert.Equal(Rig.Id, target.TaskId);
            Assert.Null(manual.TaskId);
            Assert.Equal(Admin.Id, assigned.TaskId);   // already sorted — stays
            Assert.Null(open.TaskId);                  // live block — untouched
            Assert.Null(untouched.TaskId);             // no rule hit — stays unsorted
        }

        // ---- Store roundtrip ----

        [Fact]
        public void TaskStore_Rules_Persist_And_Missing_Field_Loads_Empty()
        {
            var dir = Path.Combine(Path.GetTempPath(), "smena_rules_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var store = new TaskStore(dir);
                store.Rules.Add(new Rule { ProcessRegex = "chrome", TitleRegex = "github", TaskId = Rig.Id });
                store.Save();

                var reloaded = new TaskStore(dir);
                var rule = Assert.Single(reloaded.Rules);
                Assert.Equal("chrome", rule.ProcessRegex);
                Assert.Equal(Rig.Id, rule.TaskId);
                Assert.True(rule.Enabled);

                // a tasks.json written before the field existed deserializes to an empty list
                var legacy = Path.Combine(dir, "tasks.json");
                var old = "{\"Projects\":[],\"Tasks\":[]}";
                File.WriteAllText(legacy, old);
                var store2 = new TaskStore(dir);
                Assert.Empty(store2.Rules);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }
    }
}
