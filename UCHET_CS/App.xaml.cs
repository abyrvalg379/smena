using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;using UCHET.Models;
using UCHET.Services;
using UCHET.ViewModels;
using UCHET.Views;

namespace UCHET
{
    public partial class App : Application
    {
        private static Mutex? _singleInstance;

        public static bool IsExiting { get; private set; }

        private Poller? _poller;
        private TrayService? _tray;
        private ShellWindow? _shell;
        private WidgetWindow? _widget;
        private static bool _fatalShown;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // UCHET_ALLOW_MULTI=1 — dev/smoke flag: full startup alongside the running instance.
            bool allowMulti = Environment.GetEnvironmentVariable("UCHET_ALLOW_MULTI") == "1";
            _singleInstance = new Mutex(true, @"Local\UCHET_SingleInstance", out bool createdNew);
            if (!createdNew && !allowMulti)
            {
                ActivateExistingInstance();
                Shutdown();
                return;
            }

            DispatcherUnhandledException += (_, args) =>
            {
                try
                {
                    var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UCHET");
                    Directory.CreateDirectory(dir);
                    File.AppendAllText(Path.Combine(dir, "error.log"),
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {args.Exception}\n\n");
                }
                catch { /* best effort */ }

                if (_shell == null)
                {
                    // Startup failed: no window, no tray — a zombie would sit invisible. Show once, then die.
                    if (!_fatalShown)
                    {
                        _fatalShown = true;
                        MessageBox.Show(args.Exception.Message, "SMENA error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    Environment.Exit(1);
                    return;
                }

                MessageBox.Show(args.Exception.Message, "SMENA error", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var dir2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UCHET");
            Directory.CreateDirectory(dir2);

            var config = new ConfigManager(dir2);
            ThemeApplier.Apply(config.Current.Theme);
            var store = new TaskStore(dir2);
            if (store.Projects.Count == 0)
            {
                store.Projects.Add(new Project { Name = "General" });
                store.Save();
            }
            var log = new TimeLog(dir2);

            // recovery prompt: blocks left open by the previous run — nothing is lost silently
            foreach (var b in log.Blocks.Where(b => b.IsOpen).ToList())
            {
                var t = b.TaskId == null ? null : store.Tasks.FirstOrDefault(x => x.Id == b.TaskId);
                var name = t?.Name ?? "Unsorted";
                var res = MessageBox.Show(
                    $"Previous session detected:\n\n{name}\n{b.Start:HH:mm} — {b.End:HH:mm}\n\n" +
                    "Yes — I kept working: continue until now\nNo — stop at last activity",
                    "SMENA — recovery", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes)
                    b.End = DateTime.Now;
                b.IsOpen = false;
            }

            var matcher = new Matcher(() => store.Tasks);
            _poller = new Poller(log, matcher, config, store);

            var vm = new MainViewModel(store, log, config, _poller);
            var widget = _widget = new WidgetWindow(vm);
            var shell = _shell = new ShellWindow(vm);
            _tray = new TrayService(vm, widget, shell);

            if (!(config.Current.AutoStart && config.Current.StartMinimizedToTray))
                widget.ShowWidget();
        }

        public static void ShowDashboard() => (Current as App)?._shell?.ShowShell();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        /// <summary>
        /// Second instance: bring the first one to front ONLY if its window is already visible.
        /// A window hidden in the tray must stay hidden — forced ShowWindow made the widget
        /// "launch itself" whenever another build was test-launched.
        /// </summary>
        private static void ActivateExistingInstance()
        {
            try
            {
                int me = System.Diagnostics.Process.GetCurrentProcess().Id;
                foreach (var p in System.Diagnostics.Process.GetProcessesByName("SMENA"))
                {
                    if (p.Id == me) continue;
                    var h = p.MainWindowHandle;
                    if (h == IntPtr.Zero) { p.Refresh(); h = p.MainWindowHandle; }
                    if (h != IntPtr.Zero && IsWindowVisible(h))
                    {
                        ShowWindow(h, 9); // SW_RESTORE (no-op for normal state)
                        SetForegroundWindow(h);
                    }
                }
            }
            catch { /* best effort */ }
        }

        public static void RequestExit()
        {
            IsExiting = true;
            var app = Current;
            if (app is App a)
            {
                a._poller?.Shutdown();
                a._tray?.Dispose();
            }
            app.Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _singleInstance?.Dispose();
            base.OnExit(e);
        }
    }
}
