using Scheduler.Ui.ViewModels;
using System.Windows;

namespace Scheduler.Ui;

public partial class EditTriggerWindow : Window
{
    public EditTriggerViewModel ViewModel { get; }

    public EditTriggerWindow(EditTriggerViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
