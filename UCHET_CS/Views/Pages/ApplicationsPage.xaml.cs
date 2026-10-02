using System.Windows.Controls;
using UCHET.ViewModels;

namespace UCHET.Views
{
    public partial class ApplicationsPage : UserControl
    {
        public ApplicationsPage(MainViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}
