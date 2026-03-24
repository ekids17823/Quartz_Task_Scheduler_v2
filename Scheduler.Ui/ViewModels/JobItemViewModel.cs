using CommunityToolkit.Mvvm.ComponentModel;
using Scheduler.Core.Models;
using System;

namespace Scheduler.Ui.ViewModels;

public partial class JobItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _jobName = string.Empty;

    [ObservableProperty]
    private string _jobGroup = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayDescription))]
    private string? _description;

    public string? DisplayDescription => string.IsNullOrWhiteSpace(Description) ? Description : Description.Replace("\r", "").Replace("\n", " ");

    [ObservableProperty]
    private DateTime? _nextFireTime;

    [ObservableProperty]
    private DateTime? _previousFireTime;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActiveState))]
    [NotifyPropertyChangedFor(nameof(IsDisabledState))]
    private string _state = string.Empty;

    public bool IsActiveState => State != "已停用";
    public bool IsDisabledState => State == "已停用";

    [ObservableProperty]
    private string? _cronExpression;

    [ObservableProperty]
    private string _triggerDescription = string.Empty;

    [ObservableProperty]
    private string? _fileName;

    [ObservableProperty]
    private string? _arguments;

    [ObservableProperty]
    private string? _workingDirectory;

    [ObservableProperty]
    private int? _maxRunTimeSeconds;

    [ObservableProperty]
    private bool _misfireActionFireAndProceed = true;

    [ObservableProperty]
    private string _concurrencyRule = "Parallel";

    [ObservableProperty]
    private bool _isHidden = false;

    [ObservableProperty]
    private string _lastRunResult = "(尚未讀取紀錄)";

    [ObservableProperty]
    private string _author = string.Empty;

    [ObservableProperty]
    private DateTimeOffset _creationDate = DateTimeOffset.Now;

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<TriggerDto> _triggers = new();

    public void UpdateFromInfo(JobInfo info)
    {
        JobName = info.JobName;
        JobGroup = info.JobGroup;
        Description = info.Description;
        State = info.State;
        FileName = info.FileName;
        Arguments = info.Arguments;
        WorkingDirectory = info.WorkingDirectory;
        MaxRunTimeSeconds = info.MaxRunTimeSeconds;
        MisfireActionFireAndProceed = info.MisfireActionFireAndProceed;
        ConcurrencyRule = info.ConcurrencyRule;
        IsHidden = info.IsHidden;
        LastRunResult = info.LastRunResult ?? "(從未執行)";
        Author = info.Author ?? string.Empty;

        // 避免每 2.5 秒無條件觸發 CollectionChanged 造成 UI 點擊中斷與重新綁定 (Flicker)
        bool isTriggersChanged = Triggers.Count != info.Triggers.Count;
        if (!isTriggersChanged)
        {
            for(int i = 0; i < Triggers.Count; i++)
            {
                if (Triggers[i].TriggerName != info.Triggers[i].TriggerName ||
                    Triggers[i].NextFireTime != info.Triggers[i].NextFireTime ||
                    Triggers[i].CronExpression != info.Triggers[i].CronExpression ||
                    Triggers[i].RepeatInterval != info.Triggers[i].RepeatInterval)
                {
                    isTriggersChanged = true;
                    break;
                }
            }
        }

        if (isTriggersChanged)
        {
            Triggers.Clear();
            foreach(var t in info.Triggers)
            {
                Triggers.Add(t);
            }
        }

        var firstTrigger = System.Linq.Enumerable.FirstOrDefault(Triggers);
        NextFireTime = firstTrigger?.NextFireTime;
        PreviousFireTime = firstTrigger?.PreviousFireTime;
        CronExpression = firstTrigger?.CronExpression;

        if (Triggers.Count > 1)
        {
            TriggerDescription = "已定義多個觸發程序";
        }
        else if (Triggers.Count == 1)
        {
            TriggerDescription = Scheduler.Core.Helpers.TriggerDescriptionHelper.GetDescription(Triggers[0]);
        }
        else
        {
            TriggerDescription = "無排程 (僅手動)";
        }
    }
}
