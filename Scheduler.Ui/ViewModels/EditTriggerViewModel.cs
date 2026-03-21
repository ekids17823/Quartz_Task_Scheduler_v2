using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Scheduler.Core.Models;
using System;
using System.Windows;

namespace Scheduler.Ui.ViewModels;

public partial class EditTriggerViewModel : ObservableObject
{
    private readonly TriggerDto _original;

    [ObservableProperty]
    private DateTime? _startAtDate = DateTime.Today;

    private string _startAtTimeString = DateTime.Now.ToString("HH:mm:ss");
    public string StartAtTimeString
    {
        get => _startAtTimeString;
        set
        {
            if (TimeSpan.TryParse(value, out var time))
            {
                SetProperty(ref _startAtTimeString, time.ToString(@"hh\:mm\:ss"));
            }
            else
            {
                OnPropertyChanged(nameof(StartAtTimeString));
            }
        }
    }

    [ObservableProperty]
    private DateTime? _endAtDate;

    private string _endAtTimeString = "23:59:59";
    public string EndAtTimeString
    {
        get => _endAtTimeString;
        set
        {
            if (TimeSpan.TryParse(value, out var time))
            {
                SetProperty(ref _endAtTimeString, time.ToString(@"hh\:mm\:ss"));
            }
            else
            {
                OnPropertyChanged(nameof(EndAtTimeString));
            }
        }
    }

    [ObservableProperty]
    private bool _hasEndDate;

    [ObservableProperty]
    private bool _isRepeating;

    [ObservableProperty]
    private int _repeatIntervalMinutes = 60;

    [ObservableProperty]
    private string _cronExpression = string.Empty;

    [ObservableProperty]
    private bool _isOneTime = true;
    [ObservableProperty]
    private bool _isDaily;
    [ObservableProperty]
    private bool _isWeekly;
    [ObservableProperty]
    private bool _isMonthly;

    [ObservableProperty]
    private int _dailyInterval = 1;
    [ObservableProperty]
    private int _weeklyInterval = 1;
    
    [ObservableProperty] private bool _weekSun;
    [ObservableProperty] private bool _weekMon;
    [ObservableProperty] private bool _weekTue;
    [ObservableProperty] private bool _weekWed;
    [ObservableProperty] private bool _weekThu;
    [ObservableProperty] private bool _weekFri;
    [ObservableProperty] private bool _weekSat;

    public EditTriggerViewModel(TriggerDto? existing = null)
    {
        _original = existing ?? new TriggerDto();
        
        if (existing != null)
        {
            if (existing.StartAt.HasValue)
            {
                StartAtDate = existing.StartAt.Value.LocalDateTime.Date;
                StartAtTimeString = existing.StartAt.Value.LocalDateTime.ToString("HH:mm:ss");
            }
            if (existing.EndAt.HasValue)
            {
                HasEndDate = true;
                EndAtDate = existing.EndAt.Value.LocalDateTime.Date;
                EndAtTimeString = existing.EndAt.Value.LocalDateTime.ToString("HH:mm:ss");
            }
            if (existing.RepeatIntervalMinutes.HasValue && existing.RepeatIntervalMinutes.Value > 0)
            {
                IsRepeating = true;
                RepeatIntervalMinutes = existing.RepeatIntervalMinutes.Value;
            }
            
            CronExpression = existing.CronExpression ?? string.Empty;
            ParseCron(CronExpression);
        }
    }

    private void ParseCron(string cron)
    {
        if (string.IsNullOrWhiteSpace(cron))
        {
            IsOneTime = true;
            return;
        }

        IsOneTime = false;
        var parts = cron.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 6)
        {
            var dom = parts[3];
            var dow = parts[5];

            if (dom.StartsWith("1/"))
            {
                IsDaily = true;
                if (int.TryParse(dom.Substring(2), out int d)) DailyInterval = d;
            }
            else if ((dom == "*" || dom == "?") && (dow == "*" || dow == "?"))
            {
                IsDaily = true;
                DailyInterval = 1;
            }
            else if (dow != "*" && dow != "?")
            {
                IsWeekly = true;
                WeekSun = dow.Contains("SUN");
                WeekMon = dow.Contains("MON");
                WeekTue = dow.Contains("TUE");
                WeekWed = dow.Contains("WED");
                WeekThu = dow.Contains("THU");
                WeekFri = dow.Contains("FRI");
                WeekSat = dow.Contains("SAT");
            }
            else if (dom == "1")
            {
                IsMonthly = true;
            }
            else
            {
                // Unrecognized Cron, default to Daily as fallback since Custom is removed
                IsDaily = true;
                DailyInterval = 1;
            }
        }
    }
    
    private string GenerateCron()
    {
        if (IsOneTime) return string.Empty;

        int hour = 0; int min = 0; int sec = 0;
        if (TimeSpan.TryParse(StartAtTimeString, out var time))
        {
            hour = time.Hours; min = time.Minutes; sec = time.Seconds;
        }

        if (IsDaily)
        {
            int dInt = Math.Max(1, DailyInterval);
            return dInt == 1 ? $"{sec} {min} {hour} * * ?" : $"{sec} {min} {hour} 1/{dInt} * ?";
        }
        if (IsWeekly)
        {
            var days = new System.Collections.Generic.List<string>();
            if (WeekSun) days.Add("SUN");
            if (WeekMon) days.Add("MON");
            if (WeekTue) days.Add("TUE");
            if (WeekWed) days.Add("WED");
            if (WeekThu) days.Add("THU");
            if (WeekFri) days.Add("FRI");
            if (WeekSat) days.Add("SAT");
            if (days.Count == 0) return string.Empty;
            return $"{sec} {min} {hour} ? * {string.Join(",", days)}";
        }
        if (IsMonthly)
        {
            // MVP 簡單實作：每個月1號執行
            return $"{sec} {min} {hour} 1 * ?";
        }
        return string.Empty;
    }

    public TriggerDto ToDto()
    {
        DateTimeOffset? start = null;
        if (StartAtDate.HasValue && TimeSpan.TryParse(StartAtTimeString, out var startTime))
        {
            start = new DateTimeOffset(StartAtDate.Value.Date.Add(startTime));
        }
        
        DateTimeOffset? end = null;
        if (HasEndDate && EndAtDate.HasValue && TimeSpan.TryParse(EndAtTimeString, out var endTime))
        {
            end = new DateTimeOffset(EndAtDate.Value.Date.Add(endTime));
        }

        var finalCron = GenerateCron();

        return new TriggerDto
        {
            TriggerName = _original.TriggerName,
            TriggerGroup = _original.TriggerGroup,
            StartAt = start,
            EndAt = end,
            RepeatIntervalMinutes = IsRepeating ? RepeatIntervalMinutes : null,
            CronExpression = string.IsNullOrWhiteSpace(finalCron) ? null : finalCron.Trim(),
            State = _original.State
        };
    }

    [RelayCommand]
    private void Save(Window window)
    {
        window.DialogResult = true;
        window.Close();
    }
}
