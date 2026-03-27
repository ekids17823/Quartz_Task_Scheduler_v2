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

    public string CorrelationId => Original.CorrelationId;

    public string LevelIcon 
    {
        get => Original.EventId switch
        {
            107 or 129 or 100 or 200 or 201 or 110 => "ℹ️ 資訊",
            322 or 328 => "⚠️ 警告",
            _ => "❌ 錯誤"
        };
    }
    
    public string EventTime => Original.FireTimeUtc.ToLocalTime().ToString("yyyy/M/d tt hh:mm:ss");
    public string Duration => Original.RunTimeMs >= 1000 ? $"{Original.RunTimeMs / 1000.0:0.##} 秒" : $"{Original.RunTimeMs} 毫秒";
    public string EventId => Original.EventId.ToString();

    public string Category
    {
        get => Original.EventId switch
        {
            110 => "使用者已經觸發工作",
            107 => "排程器已觸發工作",
            129 => "已建立工作處理程序",
            100 => "工作已開始",
            200 => "動作已經啟動",
            201 => "動作已完成",
            322 => "啟動要求已遭忽略，因為執行個體已在執行中",
            328 => "動作已停止",
            _ => "動作失敗"
        };
    }

    public string OpCode
    {
        get => Original.EventId switch
        {
            107 or 129 or 200 or 322 or 328 or 110 => "資訊",
            100 => "(1)",
            201 => "(2)",
            _ => "(1)"
        };
    }

    public string Description 
    {
        get
        {
            if (Original.EventId == 322) 
                return $"工作排程器並未啟動工作 \"{Original.JobName}\"，因為相同工作的執行個體已在執行中。";
            if (Original.EventId == 328) 
                return $"工作排程器已強迫停止工作 \"{Original.JobName}\"，因為收到外部中止要求。";
            if (Original.EventId == 110) return $"使用者已經手動要求啟動工作 \"{Original.JobName}\"。";
            if (Original.EventId == 107) return $"工作排程器已針對工作 \"{Original.JobName}\" 收到要求啟動的訊號。";
            if (Original.EventId == 129) return $"工作排程器已為工作 \"{Original.JobName}\" 建立執行個體處理程序。";
            if (Original.EventId == 100) return $"工作排程器已啟動工作 \"{Original.JobName}\" 的執行個體。";
            if (Original.EventId == 200) return $"工作排程器動作已在工作 \"{Original.JobName}\" 中啟動。";
            
            string baseDesc = Original.EventId == 201 
                ? $"工作排程器於 {EventTime} 已成功完成工作 \"{Original.JobName}\"，結束代碼：{Original.ExitCode}。" 
                : $"工作排程器於 {EventTime} 未能順利完成工作 \"{Original.JobName}\"，因為執行緒或子程序回報失敗。這可能是因為找不到檔案、參數錯誤，或程式提早閃退。\n錯誤訊息：{Original.ErrorMessage}";

            return baseDesc + $"\n執行耗時：{Duration}。";
        }
    }

    public Visibility ErrorMessageVisibility => string.IsNullOrWhiteSpace(Original.ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;
}

public partial class AddJobViewModel : ObservableObject
{
    private readonly SchedulerApiService _apiService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCreationMode))]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private bool _isEditMode;
    public bool IsCreationMode => !IsEditMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string _jobName = string.Empty;

    public string WindowTitle => IsCreationMode ? "建立新排程" : $"{JobName} 內容";

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
    private bool _hasMaxRunTime;

    [ObservableProperty]
    private int _maxRunTimeValue = 1;

    [ObservableProperty]
    private int _maxRunTimeUnitIndex = 1; // 0=分鐘, 1=小時, 2=天

    [ObservableProperty]
    private bool _misfireActionFireAndProceed = false;

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
            if (existingJob.MaxRunTimeSeconds.HasValue)
            {
                HasMaxRunTime = true;
                int secs = existingJob.MaxRunTimeSeconds.Value;
                if (secs >= 86400 && secs % 86400 == 0) { MaxRunTimeValue = secs / 86400; MaxRunTimeUnitIndex = 2; }
                else if (secs >= 3600 && secs % 3600 == 0) { MaxRunTimeValue = secs / 3600; MaxRunTimeUnitIndex = 1; }
                else { MaxRunTimeValue = System.Math.Max(1, secs / 60); MaxRunTimeUnitIndex = 0; }
            }
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
                    RepeatDuration = t.RepeatDuration,
                    RepeatDurationUnit = t.RepeatDurationUnit,
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

        if (IsCreationMode)
        {
            try
            {
                var existingJobs = await _apiService.GetAllJobsAsync();
                if (existingJobs.Any(j => j.JobName == JobName && j.JobGroup == JobGroup))
                {
                    MessageBox.Show($"排程「{JobName}」已存在於群組「{JobGroup}」中，請更換名稱或群組以避免覆蓋！", "名稱重複", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            catch { }
        }

        var request = new ScheduleRequest
        {
            JobName = JobName,
            JobGroup = JobGroup,
            Description = Description,
            FileName = FileName,
            Arguments = Arguments,
            WorkingDirectory = WorkingDirectory,
            MaxRunTimeSeconds = HasMaxRunTime ? System.Math.Max(1, MaxRunTimeValue) * (MaxRunTimeUnitIndex == 0 ? 60 : (MaxRunTimeUnitIndex == 1 ? 3600 : 86400)) : null,
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
