using System;
using System.IO;
using System.Linq;
using SMENA.Models;
using SMENA.Services;
using Xunit;

namespace SMENA.Tests
{
    /// <summary>Backups: create → inspect → restore roundtrips on temp folders.</summary>
    public class BackupsTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "smena_bk_" + Guid.NewGuid().ToString("N"));

        public BackupsTests() => Directory.CreateDirectory(_dir);
        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        private void SeedData()
        {
            File.WriteAllText(Path.Combine(_dir, "settings.json"), """{"IdleTimeoutMinutes":7}""");
            File.WriteAllText(Path.Combine(_dir, "tasks.json"),
                """{"Projects":[{"Name":"P1"}],"Tasks":[{"Name":"T1"},{"Name":"T2"}]}""");
            var blocks = new[]
            {
                new ActivityBlock { Start = new DateTime(2026, 1, 5, 9, 0, 0), End = new DateTime(2026, 1, 5, 10, 0, 0) },
                new ActivityBlock { Start = new DateTime(2026, 3, 10, 9, 0, 0), End = new DateTime(2026, 3, 10, 11, 0, 0) },
            };
            File.WriteAllText(Path.Combine(_dir, "blocks.json"),
                System.Text.Json.JsonSerializer.Serialize(blocks));
        }

        [Fact]
        public void Create_Then_Inspect_Returns_Counts_Dates_Timestamp()
        {
            SeedData();
            var zip = Path.Combine(_dir, "SMENA_backup_20261002_1930.zip");
            Backups.Create(zip, _dir);

            var info = Backups.Inspect(zip);
            Assert.Equal(new DateTime(2026, 10, 2, 19, 30, 0), info.CreatedAt);
            Assert.Equal(1, info.Projects);
            Assert.Equal(2, info.Tasks);
            Assert.Equal(2, info.Blocks);
            Assert.Equal(new DateTime(2026, 1, 5, 9, 0, 0), info.FirstBlock);
            Assert.Equal(new DateTime(2026, 3, 10, 9, 0, 0), info.LastBlock);
            Assert.True(info.HasSettings);
        }

        [Fact]
        public void Inspect_Settings_Only_Zip_Gives_Zeroes()
        {
            File.WriteAllText(Path.Combine(_dir, "settings.json"), "{}");
            var zip = Path.Combine(_dir, "SMENA_backup_20261002_1930.zip");
            Backups.Create(zip, _dir);

            var info = Backups.Inspect(zip);
            Assert.True(info.HasSettings);
            Assert.Equal(0, info.Blocks);
            Assert.Equal(0, info.Tasks);
            Assert.Null(info.FirstBlock);
        }

        [Fact]
        public void Restore_Roundtrip_Writes_Files_Back()
        {
            SeedData();
            var zip = Path.Combine(_dir, "SMENA_backup_20261002_1930.zip");
            Backups.Create(zip, _dir);

            var target = Path.Combine(_dir, "restored");
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "blocks.json"), """[]""");   // overwritten by restore

            Backups.Restore(zip, target);
            Assert.True(File.Exists(Path.Combine(target, "settings.json")));
            Assert.Equal(File.ReadAllText(Path.Combine(_dir, "tasks.json")),
                         File.ReadAllText(Path.Combine(target, "tasks.json")));
            Assert.Contains("2026-01-05T09:00:00", File.ReadAllText(Path.Combine(target, "blocks.json")));
            Assert.False(File.Exists(Path.Combine(target, "blocks.json.restore_tmp")));
        }

        [Fact]
        public void CountOlder_Closed_Only_Strict_Boundary()
        {
            var cutoff = new DateTime(2026, 6, 1);
            var blocks = new[]
            {
                new ActivityBlock { Start = cutoff.AddDays(-2), End = cutoff.AddDays(-1) },   // older → victim
                new ActivityBlock { Start = cutoff.AddDays(-1), End = cutoff },               // boundary → kept (strict)
                new ActivityBlock { Start = cutoff.AddDays(-2), End = cutoff.AddDays(-1), IsOpen = true }, // live → kept
                new ActivityBlock { Start = cutoff.AddDays(1), End = cutoff.AddDays(2) },     // newer → kept
            };
            Assert.Equal(1, Backups.CountOlder(blocks, cutoff));
        }

        [Fact]
        public void SuggestName_Matches_Timestamp_Format()
        {
            Assert.Equal("SMENA_backup_20261002_1930.zip", Backups.SuggestName(new DateTime(2026, 10, 2, 19, 30, 0)));
        }
    }
}
