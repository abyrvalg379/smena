using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;using SMENA.Models;
using SMENA.Services;
using SMENA.ViewModels;
using SMENA.Views;

namespace SMENA
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

        /// <summary>Active data folder: %APPDATA%\SMENA, or SMENA_DATA_DIR when set (demo/profile isolation).</summary>
        public static string DataDir { get; private set; } = "";

        /// <summary>True for isolated profiles (SMENA_DATA_DIR set): no global hotkeys, no cross-instance activation.</summary>
        public static bool Isolated { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var dataDirOverride = Environment.GetEnvironmentVariable("SMENA_DATA_DIR")
                                  ?? Environment.GetEnvironmentVariable("UCHET_DATA_DIR");   // legacy alias
            bool isolated = !string.IsNullOrWhiteSpace(dataDirOverride);
            Isolated = isolated;
            if (isolated)
            {
                DataDir = Path.GetFullPath(dataDirOverride.Trim());
            }
            else
            {
                // 02.10 rebrand: %APPDATA%\UCHET moved to %APPDATA%\SMENA on first run of a new build.
                var defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SMENA");
                var legacyDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UCHET");
                if (!Directory.Exists(defaultDir) && Directory.Exists(legacyDir))
                {
                    try { Directory.Move(legacyDir, defaultDir); }
                    catch { /* locked or cross-volume: keep serving the legacy folder */ }
                }
                DataDir = Directory.Exists(defaultDir) || !Directory.Exists(legacyDir) ? defaultDir : legacyDir;
            }

            // SMENA_ALLOW_MULTI=1 — dev/smoke flag: full startup alongside the running instance.
            bool allowMulti = Environment.GetEnvironmentVariable("SMENA_ALLOW_MULTI") == "1"
                              || Environment.GetEnvironmentVariable("UCHET_ALLOW_MULTI") == "1";   // legacy alias
            // An isolated data dir gets its own mutex: demo and real instances may run side by side.
            var mutexName = isolated
                ? @"Local\SMENA_SingleInstance_" + Math.Abs(DataDir.GetHashCode())
                : @"Local\SMENA_SingleInstance";
            _singleInstance = new Mutex(true, mutexName, out bool createdNew);
            if (!createdNew && !allowMulti)
            {
                ActivateExistingInstance();
                Shutdown();
                return;
            }

            RenameLegacyAutostartEntry();

            DispatcherUnhandledException += (_, args) =>
            {
                try
                {
                    var dir = DataDir.Length > 0 ? DataDir
                        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SMENA");
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

            Directory.CreateDirectory(DataDir);

            var config = new ConfigManager(DataDir);
            ThemeApplier.Apply(config.Current.Theme);
            var store = new TaskStore(DataDir);
            if (store.Projects.Count == 0)
            {
                store.Projects.Add(new Project { Name = "General" });
                store.Save();
            }
            var log = new TimeLog(DataDir);

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

        /// <summary>02.10 rebrand: the Run-key autostart entry was "UCHET" — carry it over to "SMENA".</summary>
        private static void RenameLegacyAutostartEntry()
        {
            try
            {
                using var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true);
                var legacy = run?.GetValue("UCHET") as string;
                if (legacy == null) return;
                if (run.GetValue("SMENA") == null) run.SetValue("SMENA", legacy);
                run.DeleteValue("UCHET", false);
            }
            catch { /* registry optional */ }
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
        /// Isolated (demo) instances never activate anything: raising every visible window
        /// named SMENA would pop the real instance's UI too.
        /// </summary>
        private static void ActivateExistingInstance()
        {
            if (Isolated) return;
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
