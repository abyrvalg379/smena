using System.Windows;
using System.Windows.Controls;
using SMENA.ViewModels;

namespace SMENA.Views
{
    public partial class ReportsPage : UserControl
    {
        private readonly MainViewModel _vm;

        public ReportsPage(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;
        }

        private void ReportExport_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is string fmt) _vm.Reports.Export(fmt);
        }
    }
}
