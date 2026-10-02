using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace UCHET.Models
{
    public class Project
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "";
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
    }
}
