using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SMENA.Models;

namespace SMENA.Services
{
    /// <summary>
    /// Auto-assignment rules: regex on the window process and/or title assigns a block
    /// to a task. A safety net UNDER the keyword matcher — only windows the matcher
    /// missed reach the rules. First matching rule in list order wins; an invalid
    /// regex or a dead (archived/deleted) target silently skips its rule, capture
    /// must never crash.
    /// </summary>
    public static class Rules
    {
        public static bool IsMatch(string processRegex, string titleRegex, string process, string title)
        {
            try
            {
                if (processRegex.Length > 0 &&
                    !Regex.IsMatch(process ?? "", processRegex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    return false;
                if (titleRegex.Length > 0 &&
                    !Regex.IsMatch(title ?? "", titleRegex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    return false;
            }
            catch (ArgumentException)
            {
                return false; // invalid pattern — rule is inert, not fatal
            }
            return true;
        }

        /// <summary>First enabled rule whose regexes match and whose target is an active task; null = no rule hit.</summary>
        public static TrackedTask? Match(IEnumerable<Rule> rules, Func<Guid, TrackedTask?> taskById, string process, string title)
        {
            foreach (var rule in rules)
            {
                if (!rule.Enabled || rule.TaskId == Guid.Empty) continue;
                var task = taskById(rule.TaskId);
                if (task == null) continue; // archived or deleted target
                if (IsMatch(rule.ProcessRegex ?? "", rule.TitleRegex ?? "", process, title))
                    return task;
            }
            return null;
        }

        /// <summary>Retro-apply rules to history: only auto-assigned, still-unsorted, closed
        /// blocks are re-bucketed. Manual and open blocks are never touched. Returns the count.</summary>
        public static int Apply(IEnumerable<ActivityBlock> blocks, IEnumerable<Rule> rules, Func<Guid, TrackedTask?> taskById)
        {
            var ruleList = rules.ToList();
            int changed = 0;
            foreach (var b in blocks)
            {
                if (b.IsOpen || b.Assigned != "auto" || b.TaskId != null) continue;
                var task = Match(ruleList, taskById, b.Process, b.Title);
                if (task == null) continue;
                b.TaskId = task.Id;
                changed++;
            }
            return changed;
        }
    }
}
