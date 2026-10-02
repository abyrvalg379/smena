using System;
using System.Drawing;
using System.Windows.Forms;
using UCHET.ViewModels;
using UCHET.Views;

namespace UCHET.Services
{
    /// <summary>Tray icon: Open (widget) / Dashboard (shell) / Pause / Manual toggle / Exit.</summary>
    public class TrayService : IDisposable
    {
        private readonly NotifyIcon _icon;
        private readonly MainViewModel _vm;
        private readonly WidgetWindow _widget;
        private readonly ShellWindow _dashboard;
        private readonly ContextMenuStrip _menu;
        private string _lastTip = "";

        public TrayService(MainViewModel vm, WidgetWindow widget, ShellWindow dashboard)
        {
            _vm = vm;
            _widget = widget;
            _dashboard = dashboard;

            _menu = new ContextMenuStrip();
            RebuildMenu(null, null);
            _menu.Opening += RebuildMenu;

            _icon = new NotifyIcon
            {
                Icon = LoadAppIcon(),
                Text = "SMENA",
                Visible = true,
                ContextMenuStrip = _menu
            };
            _icon.DoubleClick += (_, _) => _widget.ShowFromTray();
            _vm.SecondTick += UpdateTip;
            UpdateTip();
        }

        private void UpdateTip()
        {
            var t = $"SMENA — {_vm.StatusLine} · today {_vm.TodayTotal}";
            if (t.Length > 63) t = t[..63];
            if (t == _lastTip) return;
            _lastTip = t;
            _icon.Text = t;
        }

        private static Icon LoadAppIcon()
        {
            try
            {
                var s = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Resources/uchet.ico"))?.Stream;
                if (s != null) return new Icon(s);
            }
            catch { /* fall back */ }
            return SystemIcons.Application;
        }

        private void RebuildMenu(object? sender, System.EventArgs? e)
        {
            _menu.Items.Clear();
            _menu.Items.Add("Open", null, (_, _) => _widget.ShowFromTray());
            _menu.Items.Add("Dashboard", null, (_, _) => _dashboard.ShowFromTray());
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(
                _vm.IsPaused ? "Resume tracking" : "Pause tracking",
                null, (_, _) => { _vm.TogglePause(); });
            _menu.Items.Add(
                _vm.IsManual ? "Stop manual timer" : "Start manual timer",
                null, (_, _) => { _vm.ToggleManual(); });
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("Exit", null, (_, _) => App.RequestExit());
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
            _menu.Dispose();
        }
    }
}
