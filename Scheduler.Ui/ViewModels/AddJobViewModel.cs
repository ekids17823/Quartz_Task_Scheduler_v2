using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Scheduler.Core.Models;
using Scheduler.Ui.Services;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Scheduler.Ui.ViewModels;

public class JobLogEntryViewModel
{
    public JobLogEntry Original { get; }
    public JobLogEntryViewModel(JobLogEntry entry) => Original = entry;

    public string LevelIcon 
    {
        get
        {
            if (Original.ErrorMessage != null && Original.ErrorMessage.Contains("因並發規則")) return "⚠️ 警告";
            if (Original.ErrorMessage != null && Original.ErrorMessage.Contains("強制中斷")) return "⚠️ 警告";
            return Original.IsSuccess ? "ℹ️ 資訊" : "❌ 錯誤";
        }
    }
    public string FireTime => Original.FireTimeUtc.ToLocalTime().ToString("yyyy/M/d tt hh:mm:ss");
    
    public string EventId
    {
        get
        {
            if (Original.ErrorMessage != null && Original.ErrorMessage.Contains("因並發規則")) return "322";
            if (Original.ErrorMessage != null && Original.ErrorMessage.Contains("強制中斷")) return "328";
            return Original.IsSuccess ? "201" : "203";
        }
    }

    public string Category
    {
        get
        {
            if (Original.ErrorMessage != null && Original.ErrorMessage.Contains("因並發規則")) return "啟動要求已遭忽略，因為執行個體已在執行中";
            if (Original.ErrorMessage != null && Original.ErrorMessage.Contains("強制中斷")) return "動作已停止";
            return Original.IsSuccess ? "動作已完成" : "動作失敗";
        }
    }

    public string OpCode
    {
        get
        {
            if (Original.ErrorMessage != null && Original.ErrorMessage.Contains("因並發規則")) return "資訊";
            if (Original.ErrorMessage != null && Original.ErrorMessage.Contains("強制中斷")) return "資訊";
            return Original.IsSuccess ? "(2)" : "(1)";
        }
    }

    public string Description 
    {
        get
        {
            if (Original.ErrorMessage != null && Original.ErrorMessage.Contains("因並發規則")) 
                return $"工作排程器並未啟動工作 \"{Original.JobName}\"，因為相同工作的執行個體已在執行中。";
            if (Original.ErrorMessage != null && Original.ErrorMessage.Contains("強制中斷")) 
                return $"工作排程器已強迫停止工作 \"{Original.JobName}\"，因為收到外部中止要求。";
            
            string baseDesc = Original.IsSuccess ? $"工作排程器已成功完成工作 \"{Original.JobName}\"，結束代碼：{Original.ExitCode}。" : $"工作排程器未能順利完成工作 \"{Original.JobName}\"，因為執行緒或子程序回報失敗。這可能是因為找不到檔案、參數錯誤，或程式提早閃退。\n錯誤訊息：{Original.ErrorMessage}";

            return baseDesc + $"\n詳細耗時：{Original.RunTimeMs} 毫秒。";
        }
    }

    public Visibility ErrorMessageVisibility => string.IsNullOrWhiteSpace(Original.ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;
}

public partial class AddJobViewModel : ObservableObject
{
    private readonly SchedulerApiService _apiService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCreationMode))]
    private bool _isEditMode;
    public bool IsCreationMode => !IsEditMode;

    [ObservableProperty]
    private string _jobName = string.Empty;

    [ObservableProperty]
    private string _jobGroup = "預設群組";

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _arguments = string.Empty;

    [ObservableProperty]
    private string _workingDirectory = string.Empty;

    [ObservableProperty]
    private int? _maxRunTimeSeconds;

    [ObservableProperty]
    private bool _misfireActionFireAndProceed = true;

    public ObservableCollection<string> ConcurrencyOptions { get; } = new()
    {
        "不要啟動新執行個體",
        "以平行方式執行新執行個體",
        "停止現有的執行個體"
    };

    [ObservableProperty]
    private string _selectedConcurrency = "不要啟動新執行個體";

    [ObservableProperty]
    private bool _isHidden = false;

    [ObservableProperty]
    private string _author = string.Empty;

    [ObservableProperty]
    private TriggerDto? _selectedTrigger;

    public ObservableCollection<TriggerDto> Triggers { get; } = new();
    public ObservableCollection<JobLogEntryViewModel> Logs { get; } = new();

    public AddJobViewModel(JobItemViewModel? existingJob = null)
    {
        _apiService = new SchedulerApiService();

        if (existingJob != null)
        {
            IsEditMode = true;
            JobName = existingJob.JobName;
            JobGroup = existingJob.JobGroup;
            Description = existingJob.Description ?? string.Empty;
            FileName = existingJob.FileName ?? string.Empty;
            Arguments = existingJob.Arguments ?? string.Empty;
            WorkingDirectory = existingJob.WorkingDirectory ?? string.Empty;
            MaxRunTimeSeconds = existingJob.MaxRunTimeSeconds;
            MisfireActionFireAndProceed = existingJob.MisfireActionFireAndProceed;
            IsHidden = existingJob.IsHidden;
            Author = existingJob.Author;
            
            SelectedConcurrency = existingJob.ConcurrencyRule switch
            {
                "DoNotStart" => "不要啟動新執行個體",
                "StopExisting" => "停止現有的執行個體",
                _ => "以平行方式執行新執行個體"
            };

            foreach(var t in existingJob.Triggers)
            {
                Triggers.Add(new TriggerDto
                {
                    TriggerName = t.TriggerName,
                    TriggerGroup = t.TriggerGroup,
                    Description = t.Description,
                    CronExpression = t.CronExpression,
                    StartAt = t.StartAt,
                    EndAt = t.EndAt,
                    RepeatIntervalMinutes = t.RepeatIntervalMinutes,
                    RepeatInterval = t.RepeatInterval,
                    RepeatIntervalUnit = t.RepeatIntervalUnit,
                    RepeatDurationHours = t.RepeatDurationHours,
                    WeeklyInterval = t.WeeklyInterval,
                    State = t.State
                });
            }
            
            LoadLogsAsync(existingJob.JobGroup, existingJob.JobName);
        }
        else
        {
            Author = System.Environment.UserDomainName + "\\" + System.Environment.UserName;
        }
    }

    private async void LoadLogsAsync(string group, string name)
    {
        try 
        {
            var rawLogs = await _apiService.GetJobLogsAsync(group, name);
            Logs.Clear();
            foreach(var l in rawLogs) Logs.Add(new JobLogEntryViewModel(l));
        } 
        catch { }
    }

    [RelayCommand]
    private void AddTrigger()
    {
        var vm = new EditTriggerViewModel();
        var window = new EditTriggerWindow(vm);
        if (window.ShowDialog() == true)
        {
            Triggers.Add(vm.ToDto());
        }
    }

    [RelayCommand]
    private void EditTrigger()
    {
        if (SelectedTrigger == null) return;
        var vm = new EditTriggerViewModel(SelectedTrigger);
        var window = new EditTriggerWindow(vm);
        if (window.ShowDialog() == true)
        {
            var idx = Triggers.IndexOf(SelectedTrigger);
            Triggers[idx] = vm.ToDto();
            SelectedTrigger = Triggers[idx];
        }
    }

    [RelayCommand]
    private void DeleteTrigger()
    {
        if (SelectedTrigger != null)
        {
            Triggers.Remove(SelectedTrigger);
        }
    }

    [RelayCommand]
    private async Task CreateOrUpdateAsync(Window window)
    {
        if (string.IsNullOrWhiteSpace(JobName))
        {
            MessageBox.Show("任務名稱為必填欄位。");
            return;
        }

        if (string.IsNullOrWhiteSpace(FileName))
        {
            MessageBox.Show("執行檔路徑為必填欄位。");
            return;
        }

        var request = new ScheduleRequest
        {
            JobName = JobName,
            JobGroup = JobGroup,
            Description = Description,
            FileName = FileName,
            Arguments = Arguments,
            WorkingDirectory = WorkingDirectory,
            MaxRunTimeSeconds = MaxRunTimeSeconds,
            MisfireActionFireAndProceed = MisfireActionFireAndProceed,
            IsHidden = IsHidden,
            Author = Author,
            ConcurrencyRule = SelectedConcurrency switch
            {
                "不要啟動新執行個體" => "DoNotStart",
                "停止現有的執行個體" => "StopExisting",
                _ => "Parallel"
            },
            Triggers = Triggers.ToList()
        };

        try
        {
            await _apiService.CreateJobAsync(request);
            window.DialogResult = true;
            window.Close();
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"建立/更新失敗: {ex.Message}");
        }
    }
}
