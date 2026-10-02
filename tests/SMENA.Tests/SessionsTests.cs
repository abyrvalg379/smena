using System;
using System.Collections.Generic;
using SMENA.Models;
using SMENA.Services;
using Xunit;

namespace SMENA.Tests
{
    public class SessionsTests
    {
        private static readonly Guid A = Guid.NewGuid();
        private static readonly Guid B = Guid.NewGuid();

        private static ActivityBlock Blk(DateTime start, DateTime end, Guid? task, bool open = false) =>
            new() { Start = start, End = end, Process = "maya", Title = "t", TaskId = task, IsOpen = open };

        [Fact]
        public void OpenSessionStart_MergesChainOfSameTaskBlocks()
        {
            var blocks = new List<ActivityBlock>
            {
                Blk(T(10, 0), T(10, 10), A),
                Blk(T(10, 15), T(10, 25), A),
                Blk(T(10, 25), T(10, 30), A, open: true),
            };
            Assert.Equal(T(10, 0), Sessions.OpenSessionStart(blocks, A));
        }

        [Fact]
        public void OpenSessionStart_BigGapStartsNewSession()
        {
            var blocks = new List<ActivityBlock>
            {
                Blk(T(10, 0), T(10, 10), A),
                Blk(T(10, 30), T(10, 35), A, open: true),
            };
            Assert.Equal(T(10, 30), Sessions.OpenSessionStart(blocks, A));
        }

        [Fact]
        public void OpenSessionStart_TaskSwitchResetsSession()
        {
            var blocks = new List<ActivityBlock>
            {
                Blk(T(10, 0), T(10, 10), A),
                Blk(T(10, 10), T(10, 20), B),
                Blk(T(10, 20), T(10, 25), A, open: true),
            };
            Assert.Equal(T(10, 20), Sessions.OpenSessionStart(blocks, A));
        }

        [Fact]
        public void OpenSessionStart_InactiveTaskReturnsNull()
        {
            var blocks = new List<ActivityBlock>
            {
                Blk(T(10, 0), T(10, 10), A),
                Blk(T(10, 10), T(10, 20), B, open: true),
            };
            Assert.Null(Sessions.OpenSessionStart(blocks, A));
        }

        [Fact]
        public void OpenSessionStart_SingleOpenBlock()
        {
            var blocks = new List<ActivityBlock> { Blk(T(9, 0), T(9, 5), A, open: true) };
            Assert.Equal(T(9, 0), Sessions.OpenSessionStart(blocks, A));
        }

        [Fact]
        public void SplitBlock_SplitsAtMidpoint()
        {
            var b = Blk(T(10, 0), T(11, 0), A);
            var (first, second) = Sessions.SplitBlock(b);
            Assert.Equal(T(10, 30), first.End);
            Assert.NotNull(second);
            Assert.Equal(T(10, 30), second!.Start);
            Assert.Equal(T(11, 0), second.End);
            Assert.Equal(first.TaskId, second.TaskId);
        }

        [Fact]
        public void SplitBlock_TooShort_ReturnsNullSecond()
        {
            var b = Blk(T(10, 0), T(10, 1), A);   // 1 minute — cannot split
            var (first, second) = Sessions.SplitBlock(b);
            Assert.Null(second);
            Assert.Equal(T(10, 1), first.End);
        }

        private static DateTime T(int h, int m) => new(2026, 9, 30, h, m, 0);
    }
}
