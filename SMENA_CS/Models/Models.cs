using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace SMENA.Models
{
    public class Project
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "";
        public string ColorHex { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? ArchivedAt { get; set; }
    }

    public class TrackedTask
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ProjectId { get; set; }
        public string Name { get; set; } = "";

        // Comma-separated keywords matched against window title / process name.
        // Empty = fall back to the task name itself.
        public string Keywords { get; set; } = "";

        // Optional accent color "#RRGGBB"; empty = palette hash.
        public string ColorHex { get; set; } = "";

        // Work phase tag (modeling / texturing / …) — declared once per task, rolled up in reports.
        public string Phase { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? ArchivedAt { get; set; }

        [JsonIgnore]
        public List<string> KeywordList
        {
            get
            {
                var parsed = (Keywords ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(k => k.Length > 0)
                    .ToList();
                if (parsed.Count == 0 && !string.IsNullOrWhiteSpace(Name))
                    parsed.Add(Name.Trim());
                return parsed;
            }
        }
    }

    public class ActivityBlock
    {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public string Process { get; set; } = "";
        public string Title { get; set; } = "";
        public Guid? TaskId { get; set; }

        // Blocks flushed while still open (app running / crash) are marked and closed on load.
        public bool IsOpen { get; set; }

        // Free-form session notes (bullets, one string).
        public string Note { get; set; } = "";

        // MANUAL / AUTO / IMPORTED; empty = legacy → derived from Process.
        public string Source { get; set; } = "";

        // Who linked this block to its task: auto (keyword match at capture)
        // or manual (assign drawer / merge / edit). Keeps re-classification safe.
        public string Assigned { get; set; } = "auto";

        [JsonIgnore]
        public string EffectiveSource =>
            string.IsNullOrEmpty(Source) ? (Process == "manual" ? "MANUAL" : "AUTO") : Source;

        [JsonIgnore]
        public double Minutes => (End - Start).TotalMinutes;
    }

    public class Settings
    {
        public int IdleTimeoutMinutes { get; set; } = 5;
        public bool AutoStart { get; set; }
        public bool StartMinimizedToTray { get; set; } = true;

        // compact widget
        public bool WidgetTopmost { get; set; }
        public double? WidgetLeft { get; set; }
        public double? WidgetTop { get; set; }

        // tracking (TIMETRACK spec)
        public bool AutoMode { get; set; } = true;
        public bool IdleDetection { get; set; } = true;
        public bool IdlePrompt { get; set; }
        public bool TrackFiles { get; set; } = true;

        // ThemeManager key ("smena", "blender", …)
        public string Theme { get; set; } = "smena";

        // Privacy: newline-separated substrings; a window whose title or process name
        // contains any line is not tracked at all (banking, passwords, personal stuff).
        public string Exclusions { get; set; } = "";

        // Retention for CLEAN UP NOW: delete closed blocks older than N months (0 = keep all).
        public int CleanupMonths { get; set; } = 0;
    }

    // Auto-assignment rule (RULES, settings page): regex on the window process and/or
    // title assigns the block to a task. Applied only when the keyword matcher missed.
    // Empty regex = any process/title; both non-empty = both must match.
    public class Rule
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public bool Enabled { get; set; } = true;

        // Free-form label; UI falls back to the target task name.
        public string Name { get; set; } = "";

        public string ProcessRegex { get; set; } = "";
        public string TitleRegex { get; set; } = "";
        public Guid TaskId { get; set; }
    }
}
