using System.Windows.Controls;
using UCHET.ViewModels;

namespace UCHET.Views
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
