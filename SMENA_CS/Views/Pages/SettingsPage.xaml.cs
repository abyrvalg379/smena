using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using SMENA.ViewModels;

namespace SMENA.Views
{
    public partial class SettingsPage : UserControl
    {
        private readonly MainViewModel _vm;

        public SettingsPage(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;
        }

        private void PauseToggle_Click(object sender, RoutedEventArgs e) => _vm.TogglePause();
        private void CheckUpdates_Click(object sender, RoutedEventArgs e) => _vm.CheckForUpdates(silent: false);
        private void DesktopShortcut_Click(object sender, RoutedEventArgs e) => _vm.CreateShortcut(desktop: true);
        private void StartMenuShortcut_Click(object sender, RoutedEventArgs e) => _vm.CreateShortcut(desktop: false);
        private void OpenFolder_Click(object sender, RoutedEventArgs e) =>
            Process.Start("explorer.exe", _vm.DataFolder);
        private void BackupNow_Click(object sender, RoutedEventArgs e) => _vm.BackupNow();
        private void RestoreBackup_Click(object sender, RoutedEventArgs e) => _vm.RestoreFromBackup();
        private void CleanupNow_Click(object sender, RoutedEventArgs e) => _vm.CleanupNow();
    }
}
