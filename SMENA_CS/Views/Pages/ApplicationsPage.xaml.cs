using System.Windows.Controls;
using SMENA.ViewModels;

namespace SMENA.Views
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
