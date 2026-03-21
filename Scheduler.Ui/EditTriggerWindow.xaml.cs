using System.Windows;
using Scheduler.Ui.ViewModels;
using Wpf.Ui.Controls;
using Wpf.Ui.Appearance;

namespace Scheduler.Ui;

public partial class EditTriggerWindow : FluentWindow
{
    public EditTriggerViewModel ViewModel { get; }

    public EditTriggerWindow(EditTriggerViewModel viewModel)
    {
        ApplicationThemeManager.Apply(ApplicationTheme.Light);
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
