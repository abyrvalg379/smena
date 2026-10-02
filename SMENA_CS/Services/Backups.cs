using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using SMENA.Models;

namespace SMENA.Services
{
    public record BackupInfo
    {
        public DateTime CreatedAt { get; init; }
        public int Projects { get; init; }
        public int Tasks { get; init; }
        public int Blocks { get; init; }
        public DateTime? FirstBlock { get; init; }
        public DateTime? LastBlock { get; init; }
        public bool HasSettings { get; init; }
    }

    /// <summary>
    /// Data safety: one zip with settings.json + tasks.json + blocks.json.
    /// Restore swaps the files back (extract to tmp + move, a truncated zip
    /// can't half-restore); every destructive operation in the UI keeps a
    /// safety copy here first. Pure logic lives here — unit tested.
    /// </summary>
    public static class Backups
    {
        public const string SettingsEntry = "settings.json";
        public const string TasksEntry = "tasks.json";
        public const string BlocksEntry = "blocks.json";
        private static readonly string[] EntryNames = { SettingsEntry, TasksEntry, BlocksEntry };

        public static string SuggestName(DateTime now) => $"SMENA_backup_{now:yyyyMMdd_HHmm}.zip";

        public static void Create(string zipPath, string dataDir)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath) ?? ".");
            using var fs = new FileStream(zipPath, FileMode.Create);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
            foreach (var name in EntryNames)
            {
                var path = Path.Combine(dataDir, name);
                if (File.Exists(path))
                    zip.CreateEntryFromFile(path, name);
            }
        }

        public static BackupInfo Inspect(string zipPath)
        {
            var info = new BackupInfo
            {
                CreatedAt = ParseTimestamp(zipPath) ?? File.GetLastWriteTime(zipPath),
            };
            using var fs = File.OpenRead(zipPath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);

            var settings = zip.GetEntry(SettingsEntry);
            info = info with { HasSettings = settings != null };

            var tasks = zip.GetEntry(TasksEntry);
            if (tasks != null)
            {
                using var doc = JsonDocument.Parse(EntryText(tasks));
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("Projects", out var p) && p.ValueKind == JsonValueKind.Array)
                        info = info with { Projects = p.GetArrayLength() };
                    if (doc.RootElement.TryGetProperty("Tasks", out var t) && t.ValueKind == JsonValueKind.Array)
                        info = info with { Tasks = t.GetArrayLength() };
                }
            }

            var blocks = zip.GetEntry(BlocksEntry);
            if (blocks != null)
            {
                using var doc = JsonDocument.Parse(EntryText(blocks));
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var arr = doc.RootElement;
                    info = info with { Blocks = arr.GetArrayLength() };
                    DateTime? first = null, last = null;
                    foreach (var b in arr.EnumerateArray())
                    {
                        if (b.ValueKind != JsonValueKind.Object || !b.TryGetProperty("Start", out var s)) continue;
                        var d = s.TryGetDateTime(out var dt) ? dt : (DateTime?)null;
                        if (d == null) continue;
                        first = first == null || d < first ? d : first;
                        last = last == null || d > last ? d : last;
                    }
                    info = info with { FirstBlock = first, LastBlock = last };
                }
            }
            return info;
        }

        public static void Restore(string zipPath, string dataDir)
        {
            Directory.CreateDirectory(dataDir);
            using var fs = File.OpenRead(zipPath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            foreach (var name in EntryNames)
            {
                var entry = zip.GetEntry(name);
                if (entry == null) continue;
                var target = Path.Combine(dataDir, name);
                var tmp = target + ".restore_tmp";
                entry.ExtractToFile(tmp, overwrite: true);
                File.Move(tmp, target, overwrite: true);
            }
        }

        /// <summary>Closed blocks ending before the cutoff. The live block is never a victim.</summary>
        public static int CountOlder(IEnumerable<ActivityBlock> blocks, DateTime cutoff) =>
            blocks.Count(b => !b.IsOpen && b.End < cutoff);

        private static string EntryText(ZipArchiveEntry entry)
        {
            using var s = entry.Open();
            using var r = new StreamReader(s);
            return r.ReadToEnd();
        }

        private static DateTime? ParseTimestamp(string zipPath)
        {
            // SMENA_backup_20261002_1930.zip — the name is the backup moment
            var name = Path.GetFileNameWithoutExtension(zipPath);
            var idx = name.LastIndexOf('_');
            var prev = idx > 0 ? name.LastIndexOf('_', idx - 1) : -1;
            if (prev < 0) return null;
            var stamp = name.Substring(prev + 1);
            return DateTime.TryParseExact(stamp, "yyyyMMdd_HHmm", null,
                System.Globalization.DateTimeStyles.None, out var dt) ? dt : null;
        }
    }
}
