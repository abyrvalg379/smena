using System;
using System.Linq;

namespace SMENA.Services
{
    /// <summary>Privacy exclusions: newline-separated substrings matched against the
    /// window title or process name (case-insensitive). A matching window is not tracked.</summary>
    public static class Exclusions
    {
        public static string[] Parse(string spec) =>
            (spec ?? "")
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .ToArray();

        public static bool IsMatch(string spec, string process, string title)
        {
            foreach (var e in Parse(spec))
            {
                if ((title ?? "").IndexOf(e, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if ((process ?? "").IndexOf(e, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }
    }
}
