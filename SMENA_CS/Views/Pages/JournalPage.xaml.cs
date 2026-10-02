using System.Windows;
using System.Windows.Controls;
using SMENA.ViewModels;

namespace SMENA.Views
{
    public partial class JournalPage : UserControl
    {
        private readonly MainViewModel _vm;

        public JournalPage(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;
        }

        private void JournalReopen_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is JournalRow r) _vm.ReopenTask(r.TaskId);
        }

        private void JournalDelete_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is JournalRow r)
            {
                if (MessageBox.Show($"Delete task '{r.Name}'? Its tracked time goes to Unsorted.",
                        "SMENA", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                    _vm.DeleteTaskNow(r.TaskId);
            }
        }
    }
}
