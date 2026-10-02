using System;
using System.Collections.Generic;
using System.Linq;
using SMENA.Models;
using SMENA.Services;
using Xunit;

namespace SMENA.Tests
{
    public class AggregationTests
    {
        private static ActivityBlock Block(Guid? taskId, int minutes) =>
            new() { Start = DateTime.Today.AddHours(9), End = DateTime.Today.AddHours(9).AddMinutes(minutes), TaskId = taskId };

        private readonly Project kraken = new() { Name = "KRAKEN", ColorHex = "#FF9F0A" };
        private readonly Project personal = new() { Name = "PERSONAL TOOLS" };

        private List<TrackedTask> Tasks() => new()
        {
            new TrackedTask { Id = Guid.NewGuid(), ProjectId = kraken.Id, Name = "Yermak — Modeling", Phase = "modeling" },
            new TrackedTask { Id = Guid.NewGuid(), ProjectId = kraken.Id, Name = "Yermak — Texturing", Phase = "texturing" },
            new TrackedTask { Id = Guid.NewGuid(), ProjectId = personal.Id, Name = "SMENA" },
        };

        private (Guid modeling, Guid texturing, Guid smena) Ids(List<TrackedTask> ts) =>
            (ts[0].Id, ts[1].Id, ts[2].Id);

        [Fact]
        public void BuildTree_Rolls_Tasks_Under_Projects_And_Unsorted_Last()
        {
            var ts = Tasks();
            var (modeling, texturing, smena) = Ids(ts);
            var blocks = new List<ActivityBlock>
            {
                Block(modeling, 151),       // 2h 31m
                Block(texturing, 258),      // 4h 18m
                Block(smena, 52),           // 52m
                Block(null, 14),            // Unsorted
            };

            var tree = Aggregation.BuildTree(blocks, new[] { kraken, personal }, ts);

            Assert.Equal(3, tree.Count);
            Assert.Equal("KRAKEN", tree[0].Name);
            Assert.True(tree[0].IsProject);
            Assert.Equal(258 + 151, tree[0].Minutes, 1);
            Assert.Equal(2, tree[0].Children.Count);
            Assert.Equal("Yermak — Texturing", tree[0].Children[0].Name);
            Assert.Equal("PERSONAL TOOLS", tree[1].Name);
            Assert.Equal("Unsorted", tree[2].Name);
            Assert.True(tree[2].IsUnsorted);
        }

        [Fact]
        public void BuildTree_Empty_Range_Gives_Empty_Tree()
        {
            var ts = Tasks();
            Assert.Empty(Aggregation.BuildTree(new List<ActivityBlock>(), new[] { kraken, personal }, ts));
        }

        [Fact]
        public void BuildTree_Live_View_Excludes_Archived()
        {
            var ts = Tasks();
            var (modeling, _, smena) = Ids(ts);
            ts[0].ArchivedAt = DateTime.Now;                    // archived modeling task
            var blocks = new List<ActivityBlock>
            {
                Block(modeling, 60), Block(smena, 30),
            };

            var live = Aggregation.BuildTree(blocks, new[] { kraken, personal }, ts, includeArchived: false);
            Assert.Single(live);                                 // KRAKEN dropped entirely
            Assert.Equal("PERSONAL TOOLS", live[0].Name);

            var reports = Aggregation.BuildTree(blocks, new[] { kraken, personal }, ts, includeArchived: true);
            Assert.Equal(2, reports.Count);                      // reports keep history
            Assert.Equal(60, reports.First(r => r.Name == "KRAKEN").Minutes, 1);
        }

        [Fact]
        public void BuildTree_Archived_Project_Hides_Tasks_In_Live_View()
        {
            var ts = Tasks();
            var (modeling, _, _) = Ids(ts);
            kraken.ArchivedAt = DateTime.Now;
            var blocks = new List<ActivityBlock> { Block(modeling, 45) };

            Assert.Empty(Aggregation.BuildTree(blocks, new[] { kraken, personal }, ts));
        }

        [Fact]
        public void PhaseRollup_Sums_By_Phase_Ignoring_Untagged_And_Unsorted()
        {
            var ts = Tasks();
            var (modeling, texturing, smena) = Ids(ts);
            var blocks = new List<ActivityBlock>
            {
                Block(modeling, 90), Block(texturing, 60), Block(null, 30), Block(smena, 15),
            };

            var phases = Aggregation.PhaseRollup(blocks, ts);

            Assert.Equal(2, phases.Count);
            Assert.Equal(("modeling", 90), (phases[0].Name, phases[0].Minutes));
            Assert.Equal(("texturing", 60), (phases[1].Name, phases[1].Minutes));
        }

        [Fact]
        public void FlatRows_Covers_All_Time_Including_Unsorted()
        {
            var ts = Tasks();
            var (modeling, _, _) = Ids(ts);
            var blocks = new List<ActivityBlock> { Block(modeling, 60), Block(null, 25) };

            var rows = Aggregation.FlatRows(blocks, new[] { kraken, personal }, ts);

            Assert.Equal(85, rows.Sum(r => r.Minutes), 1);
            Assert.Contains(rows, r => r.Project == "KRAKEN" && r.Task == "Yermak — Modeling" && r.Phase == "modeling");
            Assert.Contains(rows, r => r.Project == "Unsorted" && r.Minutes == 25);
        }
    }
}
