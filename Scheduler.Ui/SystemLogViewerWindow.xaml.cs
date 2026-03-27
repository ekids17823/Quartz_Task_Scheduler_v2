using System.Windows;
using System.Linq;
using Scheduler.Ui.Services;
using Scheduler.Ui.ViewModels;
using Wpf.Ui.Controls;
using Wpf.Ui.Appearance;

namespace Scheduler.Ui;

public partial class SystemLogViewerWindow : FluentWindow
{
    private readonly SchedulerApiService _apiService;

    public SystemLogViewerWindow()
    {
        ApplicationThemeManager.Apply(ApplicationTheme.Light);
        InitializeComponent();
        _apiService = new SchedulerApiService();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var logs = await _apiService.GetAuditLogsAsync();
        var formattedLogs = logs.Select(x => new SystemLogEntryViewModel(x)).ToList();
        LogsGrid.ItemsSource = formattedLogs;
        LogCountText.Text = $"系統事件數目: {formattedLogs.Count}";
    }
}
