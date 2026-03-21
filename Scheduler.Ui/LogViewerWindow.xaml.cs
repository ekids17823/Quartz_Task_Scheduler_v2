using System.Windows;
using System.Linq;
using Scheduler.Ui.Services;
using Scheduler.Ui.ViewModels;

namespace Scheduler.Ui;

public partial class LogViewerWindow : Window
{
    private readonly string _jobGroup;
    private readonly string _jobName;
    private readonly SchedulerApiService _apiService;

    public LogViewerWindow(string group, string name)
    {
        InitializeComponent();
        _jobGroup = group;
        _jobName = name;
        _apiService = new SchedulerApiService();
        Title = $"排程執行紀錄: {_jobGroup}.{_jobName}";
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var logs = await _apiService.GetJobLogsAsync(_jobGroup, _jobName);
        var formattedLogs = logs.Select(x => new JobLogEntryViewModel(x)).ToList();
        LogsGrid.ItemsSource = formattedLogs;
        LogCountText.Text = $"事件數目: {formattedLogs.Count}";
    }
}
