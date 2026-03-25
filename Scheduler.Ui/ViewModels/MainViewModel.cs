using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Scheduler.Ui.Services;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using System;

namespace Scheduler.Ui.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SchedulerApiService _apiService;

    public ObservableCollection<JobItemViewModel> Jobs { get; } = new();

    [ObservableProperty]
    private JobItemViewModel? _selectedJob;

    [ObservableProperty]
    private bool _isConnected = false;

    [ObservableProperty]
    private string _apiStatusText = "連線中...";

    private readonly DispatcherTimer _refreshTimer;

    public MainViewModel()
    {
        _apiService = new SchedulerApiService();
        _ = LoadJobsAsync();

        // 建立背景自動刷新機制
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2.5)
        };
        _refreshTimer.Tick += async (s, e) => await LoadJobsAsync();
        _refreshTimer.Start();
    }

    [RelayCommand]
    private async Task LoadJobsAsync()
    {
        try
        {
            var jobs = await _apiService.GetAllJobsAsync();

            IsConnected = true;
            ApiStatusText = "已連線";
            
            // 移除被刪除的排程
            var toRemove = Jobs.Where(old => !jobs.Any(n => n.JobName == old.JobName && n.JobGroup == old.JobGroup)).ToList();
            foreach (var old in toRemove) Jobs.Remove(old);

            // 新增或平滑更新既有排程，維持 UI 被選取狀態不閃爍
            foreach (var job in jobs)
            {
                var existing = Jobs.FirstOrDefault(old => old.JobName == job.JobName && old.JobGroup == job.JobGroup);
                if (existing != null)
                {
                    existing.UpdateFromInfo(job);
                }
                else
                {
                    var vm = new JobItemViewModel();
                    vm.UpdateFromInfo(job);
                    Jobs.Add(vm);
                }
            }
        }
        catch
        {
            IsConnected = false;
            ApiStatusText = "未連線";
            // 背景刷新失敗 (API沒開) 不必嚴重報錯
        }
    }

    [RelayCommand]
    private void OpenAddJob()
    {
        var window = new AddJobWindow();
        if (window.ShowDialog() == true)
        {
            _ = LoadJobsAsync(); // Reload jobs after addition
        }
    }

    [RelayCommand]
    private void EditJob()
    {
        if (SelectedJob == null) return;
        var window = new AddJobWindow(SelectedJob);
        if (window.ShowDialog() == true)
        {
            _ = LoadJobsAsync(); // Reload jobs after addition
        }
    }

    [RelayCommand]
    private async Task RunJobAsync()
    {
        if (SelectedJob == null) return;
        await _apiService.TriggerJobAsync(SelectedJob.JobGroup, SelectedJob.JobName);
        await LoadJobsAsync();
    }

    [RelayCommand]
    private async Task EndJobAsync()
    {
        if (SelectedJob == null) return;
        await _apiService.InterruptJobAsync(SelectedJob.JobGroup, SelectedJob.JobName);
        await LoadJobsAsync();
    }

    [RelayCommand]
    private async Task DisableJobAsync()
    {
        if (SelectedJob == null) return;
        await _apiService.PauseJobAsync(SelectedJob.JobGroup, SelectedJob.JobName);
        await LoadJobsAsync();
    }

    [RelayCommand]
    private async Task EnableJobAsync()
    {
        if (SelectedJob == null) return;
        await _apiService.ResumeJobAsync(SelectedJob.JobGroup, SelectedJob.JobName);
        await LoadJobsAsync();
    }

    [RelayCommand]
    private async Task DeleteJobAsync()
    {
        if (SelectedJob == null) return;
        await _apiService.DeleteJobAsync(SelectedJob.JobGroup, SelectedJob.JobName);
        await LoadJobsAsync();
    }

    [RelayCommand]
    private void ViewLogs()
    {
        if (SelectedJob == null) return;
        var window = new LogViewerWindow(SelectedJob.JobGroup, SelectedJob.JobName);
        window.ShowDialog();
    }
}
