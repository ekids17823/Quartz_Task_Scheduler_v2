using System;
using System.Linq;
using Scheduler.Core.Models;

namespace Scheduler.Core.Helpers;

public static class TriggerDescriptionHelper
{
    public static string GetDescription(TriggerDto trigger)
    {
        string baseDesc = string.Empty;
        var st = trigger.StartAt?.ToLocalTime().DateTime;
        var start = st?.ToString("yyyy/M/d") ?? "現在";
        var time = st?.ToString("tt hh:mm:ss") ?? "";

        if (string.IsNullOrWhiteSpace(trigger.CronExpression))
        {
            if (trigger.RepeatInterval.HasValue && trigger.RepeatInterval.Value > 0)
                baseDesc = $"從 {start} {time} 啟動循環";
            else
                baseDesc = $"於 {start} {time} 執行一次";
        }
        else
        {
            var parts = trigger.CronExpression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 6)
            {
                var hourStr = parts[2];
                var minStr = parts[1];
                var secStr = parts[0];
                string exactTime = time; 
                if (int.TryParse(hourStr, out int h) && int.TryParse(minStr, out int m) && int.TryParse(secStr, out int s))
                {
                    var dt = DateTime.Today.AddHours(h).AddMinutes(m).AddSeconds(s);
                    exactTime = dt.ToString("tt hh:mm:ss");
                }
                
                var dom = parts[3];
                var mth = parts[4];
                var dow = parts[5];

                // 處理月份翻譯
                string monthPrefix = "每個月";
                if (mth != "*" && mth != "?")
                {
                    var mthMap = new System.Collections.Generic.Dictionary<string, string> {
                        {"1", "一月"}, {"2", "二月"}, {"3", "三月"}, {"4", "四月"}, {"5", "五月"}, {"6", "六月"},
                        {"7", "七月"}, {"8", "八月"}, {"9", "九月"}, {"10", "十月"}, {"11", "十一月"}, {"12", "十二月"},
                        {"JAN", "一月"}, {"FEB", "二月"}, {"MAR", "三月"}, {"APR", "四月"}, {"MAY", "五月"}, {"JUN", "六月"},
                        {"JUL", "七月"}, {"AUG", "八月"}, {"SEP", "九月"}, {"OCT", "十月"}, {"NOV", "十一月"}, {"DEC", "十二月"}
                    };
                    var mArr = mth.Split(',').Select(x => mthMap.TryGetValue(x.ToUpper(), out var m) ? m : x);
                    monthPrefix = $"每個 {string.Join("、", mArr)}";
                }

                if (dom.StartsWith("1/")) 
                {
                    int interval = 1;
                    if (int.TryParse(dom.Substring(2), out int i)) interval = i;
                    if (interval == 1) baseDesc = $"從 {start} 開始，於每天的 {exactTime}";
                    else baseDesc = $"從 {start} 開始，每隔 {interval} 天的 {exactTime}";
                }
                else if ((dom == "*" || dom == "?") && !dow.Contains("#") && !(dow.Length > 2 && dow.EndsWith("L")) && dow != "?" && dow != "*") 
                {
                    var dowMap = new System.Collections.Generic.Dictionary<string, string>
                    {
                        {"SUN", "星期日"}, {"MON", "星期一"}, {"TUE", "星期二"}, {"WED", "星期三"},
                        {"THU", "星期四"}, {"FRI", "星期五"}, {"SAT", "星期六"}
                    };
                    var darr = dow.Split(',').Select(x => dowMap.TryGetValue(x.ToUpper(), out var c) ? c : x);
                    string dowStr = string.Join("、", darr);
                    string weeklyPrefix = trigger.WeeklyInterval.HasValue && trigger.WeeklyInterval.Value > 1 ? $"每隔 {trigger.WeeklyInterval.Value} 個" : "每個";
                    baseDesc = $"從 {start} 開始，{weeklyPrefix}星期的 {dowStr} 的 {exactTime}";
                }
                else if (dom != "*" && dom != "?") 
                {
                    string domDesc = dom == "L" ? "最後一天" : $"第 {dom.Replace(",", "、")} 天";
                    baseDesc = $"從 {start} 開始，於 {monthPrefix} 的 {domDesc} 的 {exactTime}";
                }
                else if (dow.Contains("#") || dow.Contains("L") && dow.Length > 1)
                {
                    string seq = "", dowDesc = "";
                    if (dow.EndsWith("L")) seq = "最後一個";
                    else if (dow.EndsWith("#1")) seq = "第一個";
                    else if (dow.EndsWith("#2")) seq = "第二個";
                    else if (dow.EndsWith("#3")) seq = "第三個";
                    else if (dow.EndsWith("#4")) seq = "第四個";
                    else if (dow.EndsWith("#5")) seq = "第五個";
                    
                    var dowMap = new System.Collections.Generic.Dictionary<string, string> {
                        {"1", "星期日"}, {"2", "星期一"}, {"3", "星期二"}, {"4", "星期三"},
                        {"5", "星期四"}, {"6", "星期五"}, {"7", "星期六"},
                        {"SUN", "星期日"}, {"MON", "星期一"}, {"TUE", "星期二"}, {"WED", "星期三"},
                        {"THU", "星期四"}, {"FRI", "星期五"}, {"SAT", "星期六"}
                    };
                    string dowPrefix = dow.Split('#')[0];
                    if (dowPrefix.EndsWith("L")) dowPrefix = dowPrefix.Substring(0, dowPrefix.Length - 1);
                    if (dowMap.TryGetValue(dowPrefix.ToUpper(), out string? dStr) && dStr != null) dowDesc = dStr;
                    else dowDesc = dowPrefix;
                    
                    baseDesc = $"從 {start} 開始，於 {monthPrefix} 的 {seq} {dowDesc} 的 {exactTime}";
                }
                else 
                {
                    baseDesc = $"從 {start} 開始，於每天的 {exactTime}";
                }
            }
            else
            {
                baseDesc = $"自訂排程 ({trigger.CronExpression})";
            }
        }

        int repVal = trigger.RepeatInterval ?? trigger.RepeatIntervalMinutes ?? 0;
        if (repVal > 0)
        {
            string label = trigger.RepeatIntervalUnit == "Second" ? "秒" : (trigger.RepeatIntervalUnit == "Hour" ? "小時" : "分鐘");
            baseDesc += $" - 觸發之後，每 {repVal} {label}便重複一次";
            
            if (trigger.RepeatDuration.HasValue && trigger.RepeatDuration.Value > 0)
            {
                string durLabel = trigger.RepeatDurationUnit == "Minute" ? "分鐘" : (trigger.RepeatDurationUnit == "Hour" ? "小時" : "天");
                baseDesc += $"，持續 {trigger.RepeatDuration.Value} {durLabel}";
            }
            baseDesc += "。";
        }

        return baseDesc;
    }

    public static string GetTriggerType(TriggerDto trigger)
    {
        if (string.IsNullOrWhiteSpace(trigger.CronExpression))
        {
            if (trigger.UiTabType == "OneTime") return "僅一次";
            if (trigger.UiTabType == "Interval") return "定期循環";
            
            int repVal = trigger.RepeatInterval ?? trigger.RepeatIntervalMinutes ?? 0;
            if (repVal > 0) return "定期循環";
            return "僅一次";
        }
        var parts = trigger.CronExpression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 6) {
            var dom = parts[3]; var dow = parts[5];
            
            if (dom.StartsWith("1/")) return "每天";
            if ((dom == "*" || dom == "?") && (dow == "*" || dow == "?")) return "每天";
            
            // 如果是每月設定「於」某週星期幾，dow 會包含 # (例如 SUN#1) 或是 L (例如 SUNL)
            if (dom == "?" && (dow.Contains("#") || dow.Contains("L") && dow.Length > 1)) return "每月";
            
            // 如果指定具體天數 (例如 1 或是 1,15)，且 dom 不是 * 或 ?
            if (dom != "*" && dom != "?" && !dom.Contains("/")) return "每月";
            
            // 若上面都不符合但 dow 有指定天數，代表每週
            if (dow != "*" && dow != "?") return "每週";
        }
        return "自訂排程";
    }
}
