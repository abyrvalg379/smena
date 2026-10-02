using System;
using System.Collections.Generic;
using System.Linq;
using SMENA.Models;

namespace SMENA.Services
{
    /// <summary>
    /// A session is a chain of same-task blocks where gaps stay under GapMinutes.
    /// The open (running) session is the chain ending at the currently open block.
    /// </summary>
    public static class Sessions
    {
        public const double GapMinutes = 10;

        /// <summary>Split a block at its midpoint. Returns the second half, or null if the block is too short (&lt; 2 min).</summary>
        public static (ActivityBlock First, ActivityBlock? Second) SplitBlock(ActivityBlock b)
        {
            var mid = b.Start + (b.End - b.Start) / 2;
            if ((mid - b.Start).TotalMinutes < 1 || (b.End - mid).TotalMinutes < 1)
                return (b, null);
            var second = new ActivityBlock
            {
                Start = mid,
                End = b.End,
                Process = b.Process,
                Title = b.Title,
                TaskId = b.TaskId,
                Note = b.Note,
                Source = b.Source
            };
            b.End = mid;
            return (b, second);
        }

        /// <summary>Start of the running session for taskId, or null if that task is not active right now.</summary>
        public static DateTime? OpenSessionStart(IReadOnlyList<ActivityBlock> ordered, Guid? taskId)
        {
            var open = ordered.LastOrDefault(b => b.IsOpen);
            if (open == null || open.TaskId != taskId) return null;

            var start = open.Start;
            for (int j = ordered.Count - 2; j >= 0; j--)
            {
                var b = ordered[j];
                if (b.End <= start.AddMinutes(-GapMinutes)) break;                       // chain gap too big
                if (b.TaskId != taskId)
                {
                    if (b.End > start.AddMinutes(-GapMinutes)) break;                    // switched away — session resets
                    continue;
                }
                start = b.Start < start ? b.Start : start;
            }
            return start;
        }
    }
}
