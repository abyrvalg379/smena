using System;
using System.Collections.Generic;
using UCHET.Models;
using UCHET.Services;
using Xunit;

namespace UCHET.Tests
{
    public class TimelineTests
    {
        [Fact]
        public void Window_Empty_ReturnsSixHoursAroundNow()
        {
            var now = new DateTime(2026, 9, 30, 14, 20, 0);
            var (start, end) = Timeline.Window(new List<ActivityBlock>(), now);
            Assert.Equal(new DateTime(2026, 9, 30, 11, 0, 0), start);
            Assert.Equal(new DateTime(2026, 9, 30, 17, 0, 0), end);
        }

        [Fact]
        public void Window_PadsOneHourBeforeFirst_AndAfterLast()
        {
            var blocks = new List<ActivityBlock>
            {
                new() { Start = T(9, 20), End = T(10, 10) },
                new() { Start = T(14, 0), End = T(15, 10) },
            };
            var now = new DateTime(2026, 9, 30, 15, 0, 0);
            var (start, end) = Timeline.Window(blocks, now);
            Assert.Equal(new DateTime(2026, 9, 30, 8, 0, 0), start);
            Assert.Equal(new DateTime(2026, 9, 30, 16, 0, 0), end);
        }

        [Fact]
        public void Window_ExtendsToNow_AndClampsMinimumSpan()
        {
            var blocks = new List<ActivityBlock>
            {
                new() { Start = T(12, 0), End = T(12, 40) },
            };
            var now = new DateTime(2026, 9, 30, 13, 10, 0); // last block ended 30 min ago
            var (start, end) = Timeline.Window(blocks, now);
            Assert.Equal(new DateTime(2026, 9, 30, 11, 0, 0), start);
            Assert.Equal(new DateTime(2026, 9, 30, 17, 0, 0), end); // 6h span minimum
        }

        private static DateTime T(int h, int m) => new(2026, 9, 30, h, m, 0);
    }
}
