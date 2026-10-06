using System;
using System.IO;
using SMENA.Models;
using SMENA.Services;
using SMENA.ViewModels;
using Xunit;

namespace SMENA.Tests
{
    public class WidgetVariantTests : IDisposable
    {
        private readonly string _dir;
        private readonly ConfigManager _config;

        public WidgetVariantTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "smena_wv_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _config = new ConfigManager(_dir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        private MainViewModel MakeVm()
        {
            // settings region under test only touches _config; the same construction
            // pattern as ManualOffsetTests (timers never fire in xunit)
            var store = new TaskStore(_dir);
            var log = new TimeLog(_dir);
            var poller = new Poller(log, new Matcher(() => store.ActiveTasks()), _config, store);
            return new MainViewModel(store, log, _config, poller);
        }

        [Fact]
        public void Default_Variant_Is_Classic()
        {
            var vm = MakeVm();
            Assert.Equal("classic", vm.WidgetVariant);
            Assert.True(vm.WidgetHasFullHeader);
            Assert.True(vm.WidgetHasStrip);
            Assert.Equal(380, vm.WidgetPixelWidth);
        }

        [Theory]
        [InlineData("minimal", 280, true, false)]
        [InlineData("board", 160, false, false)]
        [InlineData("classic", 380, true, true)]
        public void Variant_Sets_Derived_Properties(string variant, double width, bool header, bool strip)
        {
            var vm = MakeVm();
            vm.WidgetVariant = variant;
            Assert.Equal(width, vm.WidgetPixelWidth);
            Assert.Equal(header, vm.WidgetHasFullHeader);
            Assert.Equal(strip, vm.WidgetHasStrip);
        }

        [Fact]
        public void Garbage_Variant_Normalizes_To_Classic()
        {
            _config.Current.WidgetVariant = "fancy";
            _config.Save();
            var vm = MakeVm();
            Assert.Equal("classic", vm.WidgetVariant);
            Assert.Equal(380, vm.WidgetPixelWidth);
        }

        [Fact]
        public void Cycle_Order_Classic_Minimal_Board()
        {
            var vm = MakeVm();
            vm.CycleWidgetVariant();
            Assert.Equal("minimal", vm.WidgetVariant);
            vm.CycleWidgetVariant();
            Assert.Equal("board", vm.WidgetVariant);
            vm.CycleWidgetVariant();
            Assert.Equal("classic", vm.WidgetVariant);
        }

        [Fact]
        public void Variant_Persists_To_Settings_Json()
        {
            var vm = MakeVm();
            vm.WidgetVariant = "board";
            var reloaded = new ConfigManager(_dir);
            Assert.Equal("board", reloaded.Current.WidgetVariant);
        }
    }
}
