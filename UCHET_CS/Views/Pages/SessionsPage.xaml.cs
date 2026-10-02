using System.Windows;
using System.Windows.Controls;
using UCHET.ViewModels;

namespace UCHET.Views
{
    public partial class SessionsPage : UserControl
    {
        private readonly MainViewModel _vm;

        public SessionsPage(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;
        }

        private void SaveNote_Click(object sender, RoutedEventArgs e) => _vm.SaveSessionNote();
        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedSession != null) _vm.BeginEditBlock(_vm.SelectedSession);
        }
        private void Assign_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedSession != null) _vm.BeginAssign(_vm.SelectedSession);
        }
        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.SelectedSession == null) return;
            _vm.BeginEditBlock(_vm.SelectedSession);
            _vm.DeleteEditingBlocks();
        }
    }
}
