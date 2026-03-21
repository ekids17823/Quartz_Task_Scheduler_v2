using System.Windows;
using Scheduler.Core.Models;
using Scheduler.Ui.Services;
using Scheduler.Ui.ViewModels;
using Wpf.Ui.Controls;
using Wpf.Ui.Appearance;

namespace Scheduler.Ui;

public partial class AddJobWindow : FluentWindow
{
    public AddJobWindow(JobItemViewModel? existingJob = null)
    {
        InitializeComponent();
        ApplicationThemeManager.Apply(ApplicationTheme.Light);
        DataContext = new AddJobViewModel(existingJob);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        this.DialogResult = false;
        this.Close();
    }
}
