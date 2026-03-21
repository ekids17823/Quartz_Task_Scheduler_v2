using System.Windows;
using Scheduler.Ui.ViewModels;

namespace Scheduler.Ui;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void DataGridRow_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var vm = DataContext as MainViewModel;
        if (vm != null && vm.EditJobCommand.CanExecute(null))
        {
            vm.EditJobCommand.Execute(null);
        }
    }
}