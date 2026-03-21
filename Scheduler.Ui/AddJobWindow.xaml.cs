using System.Windows;
using Scheduler.Core.Models;
using Scheduler.Ui.Services;
using Scheduler.Ui.ViewModels;

namespace Scheduler.Ui;

public partial class AddJobWindow : Window
{
    public AddJobViewModel ViewModel { get; }

    public AddJobWindow(JobItemViewModel? existingJob = null)
    {
        InitializeComponent();
        ViewModel = new AddJobViewModel(existingJob);
        DataContext = ViewModel;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        this.DialogResult = false;
        this.Close();
    }
}
