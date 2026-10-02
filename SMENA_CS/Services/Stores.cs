using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SMENA.Models;

namespace SMENA.Services
{
    /// <summary>Task/keyword matching: window title or process name -> TrackedTask. Longest keyword wins.</summary>
    public class Matcher
    {
        private readonly Func<IEnumerable<TrackedTask>> _tasksProvider;

        public Matcher(Func<IEnumerable<TrackedTask>> tasksProvider)
        {
            _tasksProvider = tasksProvider;
        }

        public TrackedTask? Match(string processName, string windowTitle)
        {
            // Title hits outrank process-name hits (a scene keyword must beat a generic
            // app keyword like "maya"); longest keyword wins within a tier.
            TrackedTask? best = null;
            int bestTier = int.MaxValue;
            int bestLen = 0;

            foreach (var task in _tasksProvider())
            {
                foreach (var kw in task.KeywordList)
                {
                    int tier =
                        windowTitle.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0 ? 0 :
                        string.Equals(processName, kw, StringComparison.OrdinalIgnoreCase) ? 1 :
                        int.MaxValue;
                    bool better = tier < bestTier || (tier == bestTier && kw.Length > bestLen);
                    if (tier != int.MaxValue && better)
                    {
                        best = task;
                        bestTier = tier;
                        bestLen = kw.Length;
                    }
                }
            }
            return best;
        }
    }

    /// <summary>Settings persistence (settings.json).</summary>
    public class ConfigManager
    {
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        public string Dir { get; }
        public string FilePath => Path.Combine(Dir, "settings.json");
        public Settings Current { get; private set; } = new();

        public ConfigManager(string dir)
        {
            Dir = dir;
            Directory.CreateDirectory(dir);
            Load();
        }

        public void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    Current = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
            }
            catch
            {
                Current = new Settings();
            }
        }

        public void Save()
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, JsonOpts));
        }
    }

    /// <summary>Projects + tasks persistence (tasks.json).</summary>
    public class TaskStore
    {
        private class Storage
        {
            public List<Project> Projects { get; set; } = new();
            public List<TrackedTask> Tasks { get; set; } = new();
        }

        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        public string FilePath { get; }
        public List<Project> Projects { get; private set; } = new();
        public List<TrackedTask> Tasks { get; private set; } = new();

        public TaskStore(string dir)
        {
            FilePath = Path.Combine(dir, "tasks.json");
            Load();
        }

        public void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = JsonSerializer.Deserialize<Storage>(File.ReadAllText(FilePath));
                    if (s != null) { Projects = s.Projects; Tasks = s.Tasks; }
                }
            }
            catch
            {
                Projects = new List<Project>();
                Tasks = new List<TrackedTask>();
            }
        }

        public void Save()
        {
            var s = new Storage { Projects = Projects, Tasks = Tasks };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(s, JsonOpts));
        }

        public string? ProjectName(Guid? projectId) =>
            Projects.FirstOrDefault(p => p.Id == projectId)?.Name;
    }

    /// <summary>Append/rewrite time blocks (blocks.json). One file, rewritten on save — volumes are small.</summary>
    public class TimeLog
    {
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        public string FilePath { get; }
        public List<ActivityBlock> Blocks { get; private set; } = new();

        public TimeLog(string dir)
        {
            FilePath = Path.Combine(dir, "blocks.json");
            Load();
        }

        private void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    Blocks = JsonSerializer.Deserialize<List<ActivityBlock>>(File.ReadAllText(FilePath)) ?? new List<ActivityBlock>();
            }
            catch
            {
                Blocks = new List<ActivityBlock>();
            }
            // IsOpen blocks are preserved (recovery prompt decides their fate); nothing is lost.
        }

        /// <summary>Mark leftover open blocks closed (call after the recovery prompt is answered).</summary>
        public void CloseOpenBlocks()
        {
            foreach (var b in Blocks.Where(b => b.IsOpen))
                b.IsOpen = false;
        }

        public void Add(ActivityBlock block)
        {
            Blocks.Add(block);
            Save();
        }

        public void Save()
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Blocks, JsonOpts));
        }
    }
}
