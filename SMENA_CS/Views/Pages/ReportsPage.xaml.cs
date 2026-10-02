using System.Windows.Controls;
using SMENA.ViewModels;

namespace SMENA.Views
{
    public partial class ReportsPage : UserControl
    {
        public ReportsPage(MainViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}
