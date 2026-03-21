using Scheduler.Ui.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using System.Windows.Input;

namespace Scheduler.Ui
{
    public partial class MainWindow : FluentWindow
    {
        public MainWindow()
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Light);
            InitializeComponent();
        }

        private void DataGridRow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.EditJobCommand.CanExecute(null))
            {
                vm.EditJobCommand.Execute(null);
            }
        }
    }
}