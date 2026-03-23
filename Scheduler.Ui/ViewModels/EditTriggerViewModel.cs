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
    private int _repeatInterval = 1;
    
    [ObservableProperty]
    private int _repeatIntervalUnitIndex = 0; // 0=分鐘, 1=小時

    [ObservableProperty]
    private bool _hasRepeatDuration;

    [ObservableProperty]
    private int _repeatDuration = 1;

    [ObservableProperty]
    private int _repeatDurationUnitIndex = 1; // 0=分鐘, 1=小時, 2=天

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

    [ObservableProperty] private bool _monthJan = true;
    [ObservableProperty] private bool _monthFeb = true;
    [ObservableProperty] private bool _monthMar = true;
    [ObservableProperty] private bool _monthApr = true;
    [ObservableProperty] private bool _monthMay = true;
    [ObservableProperty] private bool _monthJun = true;
    [ObservableProperty] private bool _monthJul = true;
    [ObservableProperty] private bool _monthAug = true;
    [ObservableProperty] private bool _monthSep = true;
    [ObservableProperty] private bool _monthOct = true;
    [ObservableProperty] private bool _monthNov = true;
    [ObservableProperty] private bool _monthDec = true;

    [ObservableProperty] private bool _isMonthlyModeDays = true;
    [ObservableProperty] private string _monthlyDaysText = "1";
    
    [ObservableProperty] private bool _isMonthlyModeOn = false;
    [ObservableProperty] private int _monthlyOnSeqIndex = 0;
    [ObservableProperty] private int _monthlyOnDowIndex = 1;
    [ObservableProperty] private bool _isAllMonthsSelected = true;
    
    private bool _suppressMonthUpdates;

    [RelayCommand]
    private void ToggleAllMonths()
    {
        _suppressMonthUpdates = true;
        foreach (var m in MonthsList) m.IsSelected = IsAllMonthsSelected;
        _suppressMonthUpdates = false;
        UpdateMonthsSummary();
    }

    public System.Collections.ObjectModel.ObservableCollection<MonthDayItem> MonthsList { get; } = new();
    [ObservableProperty] private string _monthsSummary = "所有月份";

    private void UpdateMonthsSummary()
    {
        if (_suppressMonthUpdates) return;
        var sel = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(MonthsList, x => x.IsSelected));
        
        _suppressMonthUpdates = true;
        IsAllMonthsSelected = sel.Count == 12;
        _suppressMonthUpdates = false;
        if (sel.Count == 12) MonthsSummary = "所有月份";
        else if (sel.Count == 0) MonthsSummary = "未選擇";
        else MonthsSummary = string.Join(",", System.Linq.Enumerable.Select(sel, x => x.Display));
        
        MonthJan = MonthsList[0].IsSelected;
        MonthFeb = MonthsList[1].IsSelected;
        MonthMar = MonthsList[2].IsSelected;
        MonthApr = MonthsList[3].IsSelected;
        MonthMay = MonthsList[4].IsSelected;
        MonthJun = MonthsList[5].IsSelected;
        MonthJul = MonthsList[6].IsSelected;
        MonthAug = MonthsList[7].IsSelected;
        MonthSep = MonthsList[8].IsSelected;
        MonthOct = MonthsList[9].IsSelected;
        MonthNov = MonthsList[10].IsSelected;
        MonthDec = MonthsList[11].IsSelected;
    }

    public System.Collections.ObjectModel.ObservableCollection<MonthDayItem> MonthDaysList { get; } = new();
    [ObservableProperty] private string _monthDaysSummary = "1";

    private void UpdateMonthDaysSummary()
    {
        var sel = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(MonthDaysList, x => x.IsSelected));
        MonthDaysSummary = sel.Count == 0 ? "未選擇" : string.Join(",", System.Linq.Enumerable.Select(sel, x => x.Display));
        MonthlyDaysText = sel.Count == 0 ? "1" : string.Join(",", System.Linq.Enumerable.Select(sel, x => x.Value));
    }

    public EditTriggerViewModel(TriggerDto? existing = null)
    {
        string[] mNames = {"一月","二月","三月","四月","五月","六月","七月","八月","九月","十月","十一月","十二月"};
        for (int i=0; i<12; i++) MonthsList.Add(new MonthDayItem(mNames[i], (i+1).ToString(), UpdateMonthsSummary));
        foreach(var m in MonthsList) m.IsSelected = true;

        for (int i = 1; i <= 31; i++) MonthDaysList.Add(new MonthDayItem(i.ToString(), i.ToString(), UpdateMonthDaysSummary));
        MonthDaysList.Add(new MonthDayItem("最後", "L", UpdateMonthDaysSummary));
        MonthDaysList[0].IsSelected = true;

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
            if (existing.WeeklyInterval.HasValue && existing.WeeklyInterval.Value > 1)
            {
                WeeklyInterval = existing.WeeklyInterval.Value;
            }
            int repVal = existing.RepeatInterval ?? existing.RepeatIntervalMinutes ?? 0;
            if (repVal > 0)
            {
                IsRepeating = true;
                RepeatInterval = Math.Max(1, repVal);
                if (existing.RepeatIntervalUnit == "Hour") RepeatIntervalUnitIndex = 1;
                else RepeatIntervalUnitIndex = 0;
            }
            if (existing.RepeatDuration.HasValue && existing.RepeatDuration.Value > 0)
            {
                HasRepeatDuration = true;
                RepeatDuration = Math.Max(1, existing.RepeatDuration.Value);
                if (existing.RepeatDurationUnit == "Minute") RepeatDurationUnitIndex = 0;
                else if (existing.RepeatDurationUnit == "Hour") RepeatDurationUnitIndex = 1;
                else RepeatDurationUnitIndex = 2;
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
            var mon = parts[4];
            var dow = parts[5];

            if (mon != "*" && mon != "?")
            {
                var dict = new System.Collections.Generic.HashSet<string>(mon.Split(','));
                MonthsList[0].IsSelected = dict.Contains("1") || dict.Contains("JAN");
                MonthsList[1].IsSelected = dict.Contains("2") || dict.Contains("FEB");
                MonthsList[2].IsSelected = dict.Contains("3") || dict.Contains("MAR");
                MonthsList[3].IsSelected = dict.Contains("4") || dict.Contains("APR");
                MonthsList[4].IsSelected = dict.Contains("5") || dict.Contains("MAY");
                MonthsList[5].IsSelected = dict.Contains("6") || dict.Contains("JUN");
                MonthsList[6].IsSelected = dict.Contains("7") || dict.Contains("JUL");
                MonthsList[7].IsSelected = dict.Contains("8") || dict.Contains("AUG");
                MonthsList[8].IsSelected = dict.Contains("9") || dict.Contains("SEP");
                MonthsList[9].IsSelected = dict.Contains("10") || dict.Contains("OCT");
                MonthsList[10].IsSelected = dict.Contains("11") || dict.Contains("NOV");
                MonthsList[11].IsSelected = dict.Contains("12") || dict.Contains("DEC");
            }

            if (dom.StartsWith("1/"))
            {
                IsDaily = true;
                if (int.TryParse(dom.Substring(2), out int d)) DailyInterval = d;
            }
            else if ((dom == "*" || dom == "?") && (dow == "*" || dow == "?") && (mon == "*" || mon == "?"))
            {
                IsDaily = true;
                DailyInterval = 1;
            }
            else if (dow != "*" && dow != "?" && !dow.Contains("#") && !dow.EndsWith("L"))
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
            else if (dow != "*" && dow != "?" && (dow.Contains("#") || dow.EndsWith("L")))
            {
                IsMonthly = true;
                IsMonthlyModeOn = true;
                string dw = dow.Substring(0, 3);
                string[] dows = { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" };
                MonthlyOnDowIndex = Math.Max(0, Array.IndexOf(dows, dw));
                
                if (dow.EndsWith("L")) MonthlyOnSeqIndex = 4;
                else if (dow.EndsWith("#1")) MonthlyOnSeqIndex = 0;
                else if (dow.EndsWith("#2")) MonthlyOnSeqIndex = 1;
                else if (dow.EndsWith("#3")) MonthlyOnSeqIndex = 2;
                else if (dow.EndsWith("#4")) MonthlyOnSeqIndex = 3;
            }
            else if (dom != "*" && dom != "?")
            {
                IsMonthly = true;
                IsMonthlyModeDays = true;
                MonthlyDaysText = dom;
                var days = new System.Collections.Generic.HashSet<string>(dom.Split(','));
                foreach(var md in MonthDaysList) md.IsSelected = days.Contains(md.Value);
            }
            else
            {
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
            var mList = new System.Collections.Generic.List<string>();
            if (MonthJan) mList.Add("1");
            if (MonthFeb) mList.Add("2");
            if (MonthMar) mList.Add("3");
            if (MonthApr) mList.Add("4");
            if (MonthMay) mList.Add("5");
            if (MonthJun) mList.Add("6");
            if (MonthJul) mList.Add("7");
            if (MonthAug) mList.Add("8");
            if (MonthSep) mList.Add("9");
            if (MonthOct) mList.Add("10");
            if (MonthNov) mList.Add("11");
            if (MonthDec) mList.Add("12");
            
            string mStr = mList.Count == 0 || mList.Count == 12 ? "*" : string.Join(",", mList);

            if (IsMonthlyModeDays)
            {
                string dStr = string.IsNullOrWhiteSpace(MonthlyDaysText) ? "1" : MonthlyDaysText.Trim();
                if (dStr.Equals("最後一天", StringComparison.OrdinalIgnoreCase) || dStr.Equals("L", StringComparison.OrdinalIgnoreCase)) dStr = "L";
                return $"{sec} {min} {hour} {dStr} {mStr} ?";
            }
            else
            {
                string[] seqs = { "#1", "#2", "#3", "#4", "L" };
                string[] dows = { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" };
                string dow = dows[MonthlyOnDowIndex] + seqs[MonthlyOnSeqIndex];
                return $"{sec} {min} {hour} ? {mStr} {dow}";
            }
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
            RepeatInterval = IsRepeating ? Math.Max(1, RepeatInterval) : null,
            RepeatIntervalUnit = IsRepeating ? (RepeatIntervalUnitIndex == 0 ? "Minute" : "Hour") : null,
            RepeatDuration = HasRepeatDuration ? Math.Max(1, RepeatDuration) : null,
            RepeatDurationUnit = HasRepeatDuration ? (RepeatDurationUnitIndex == 0 ? "Minute" : (RepeatDurationUnitIndex == 1 ? "Hour" : "Day")) : null,
            WeeklyInterval = IsWeekly && WeeklyInterval > 1 ? WeeklyInterval : null,
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

public partial class MonthDayItem : ObservableObject
{
    public string Display { get; }
    public string Value { get; }
    
    [ObservableProperty]
    private bool _isSelected;
    
    private readonly Action _onSelectionChanged;
    
    public MonthDayItem(string display, string value, Action onSelectionChanged)
    {
        Display = display;
        Value = value;
        _onSelectionChanged = onSelectionChanged;
    }
    
    partial void OnIsSelectedChanged(bool value) => _onSelectionChanged?.Invoke();
}
