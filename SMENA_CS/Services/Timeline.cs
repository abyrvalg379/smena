using System;
using System.Collections.Generic;
using System.Linq;
using SMENA.Models;

namespace SMENA.Services
{
    /// <summary>Time window for the day timeline strip.</summary>
    public static class Timeline
    {
        public static (DateTime Start, DateTime End) Window(IReadOnlyList<ActivityBlock> blocks, DateTime now)
        {
            if (blocks.Count == 0)
            {
                var s0 = new DateTime(now.Year, now.Month, now.Day, Math.Max(0, now.Hour - 3), 0, 0);
                return (s0, s0.AddHours(6));
            }

            var s = blocks.Min(b => b.Start);
            var e = blocks.Max(b => b.End);
            if (e < now) e = now;

            var start = new DateTime(s.Year, s.Month, s.Day, s.Hour, 0, 0).AddHours(-1);
            var endCeil = e.AddMinutes(30);
            var end = new DateTime(endCeil.Year, endCeil.Month, endCeil.Day, endCeil.Hour, 0, 0).AddHours(1);
            if (end <= start) end = start.AddHours(1);
            if ((end - start).TotalHours < 6) end = start.AddHours(6);
            return (start, end);
        }
    }
}
