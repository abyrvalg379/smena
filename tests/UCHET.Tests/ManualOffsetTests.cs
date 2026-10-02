using System;
using System.IO;
using UCHET.Models;
using UCHET.Services;
using Xunit;

namespace UCHET.Tests
{
    public class ManualOffsetTests
    {
        [Fact]
        public void StartManualWithOffset_PersistsBlockWithOffsetOnShutdown()
        {
            var dir = Path.Combine(Path.GetTempPath(), "uchet_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var config = new ConfigManager(dir);
                var store = new TaskStore(dir);
                var task = new TrackedTask { Name = "TB-3" };
                store.Tasks.Add(task);

                var log = new TimeLog(dir);
                var poller = new Poller(log, new Matcher(() => store.Tasks), config, store);
                poller.StartManual(task.Id, task.Name, TimeSpan.FromHours(3));
                poller.Shutdown();   // app exit while manual timer runs — block must survive

                var reloaded = new TimeLog(dir);
                var block = Assert.Single(reloaded.Blocks, b => b.TaskId == task.Id);
                Assert.True(block.Minutes >= 180, $"expected >= 180 min, got {block.Minutes}");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
