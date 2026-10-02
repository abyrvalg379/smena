using System;
using System.Collections.Generic;
using SMENA.Models;
using SMENA.Services;
using Xunit;

namespace SMENA.Tests
{
    public class CalendarTests
    {
        private static ActivityBlock Block(DateTime start, DateTime end, bool open = false) =>
            new() { Start = start, End = end, IsOpen = open };

        [Fact]
        public void Build_Grid_Is_Monday_First_And_Covers_Month()
        {
            // October 2026: the 1st is Thursday → grid starts Mon 29.09, ends Sun 01.11
            var m = Calendar.Build(2026, 10, Array.Empty<ActivityBlock>());
            Assert.Equal(new DateTime(2026, 9, 28), m.Days[0].Date);
            Assert.Equal(35, m.Days.Count);                       // whole weeks only
            Assert.Equal(DayOfWeek.Monday, m.Days[0].Date.DayOfWeek);
            Assert.Equal(31, m.Days.Count(d => d.InMonth));
            Assert.All(m.Days, d => Assert.Equal(0, d.Minutes));
        }

        [Fact]
        public void Build_Totals_Count_InMonth_Days_Only()
        {
            var blocks = new[]
            {
                Block(new DateTime(2026, 10, 5, 10, 0, 0), new DateTime(2026, 10, 5, 12, 0, 0)),   // 120
                Block(new DateTime(2026, 10, 6, 10, 0, 0), new DateTime(2026, 10, 6, 13, 0, 0)),   // 180 — best
                Block(new DateTime(2026, 9, 29, 9, 0, 0), new DateTime(2026, 9, 29, 19, 0, 0)),    // grid cell, not October
            };
            var m = Calendar.Build(2026, 10, blocks);
            Assert.Equal(300, m.TotalMinutes);
            Assert.Equal(2, m.ActiveDays);
            Assert.Equal(180, m.BestDayMinutes);
            Assert.Equal(new DateTime(2026, 10, 6), m.BestDayDate);
        }

        [Fact]
        public void MinutesByDay_Splits_Block_Across_Midnight()
        {
            var blocks = new[]
            {
                Block(new DateTime(2026, 10, 5, 23, 30, 0), new DateTime(2026, 10, 6, 0, 30, 0)),
            };
            var map = Calendar.MinutesByDay(blocks);
            Assert.Equal(30, map[new DateTime(2026, 10, 5)]);
            Assert.Equal(30, map[new DateTime(2026, 10, 6)]);
        }

        [Fact]
        public void MinutesByDay_Skips_Open_And_Inverted_Blocks()
        {
            var blocks = new[]
            {
                Block(new DateTime(2026, 10, 5, 10, 0, 0), new DateTime(2026, 10, 5, 11, 0, 0), open: true),
                Block(new DateTime(2026, 10, 5, 12, 0, 0), new DateTime(2026, 10, 5, 11, 0, 0)),   // inverted
            };
            Assert.Empty(Calendar.MinutesByDay(blocks));
        }
    }
}
