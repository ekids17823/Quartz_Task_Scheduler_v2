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
                var dow = parts[5];

                if (dom.StartsWith("1/")) 
                {
                    int interval = 1;
                    if (int.TryParse(dom.Substring(2), out int i)) interval = i;
                    if (interval == 1) baseDesc = $"於每天 {exactTime}";
                    else baseDesc = $"每隔 {interval} 天的 {exactTime}";
                }
                else if (dom == "*" || dom == "?") 
                {
                    if (dow == "*" || dow == "?")
                    {
                        baseDesc = $"於每天 {exactTime}";
                    }
                    else
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
                }
                else if (dom != "*" && dom != "?") 
                {
                    string mStr = (parts[4] == "*" || parts[4] == "?") ? "每個月份" : "指定月份";
                    string domDesc = dom == "L" ? "最後一天" : $"第 {(dom.Contains(",") ? dom : dom)} 天";
                    baseDesc = $"{mStr}的 {domDesc} 的 {exactTime}";
                }
                else if (dow != "*" && dow != "?" && (dow.Contains("#") || dow.EndsWith("L")))
                {
                    string mStr = (parts[4] == "*" || parts[4] == "?") ? "每個月份" : "指定月份";
                    string seq = "", dowDesc = "";
                    if (dow.EndsWith("L")) seq = "最後一個";
                    else if (dow.EndsWith("#1")) seq = "第一個";
                    else if (dow.EndsWith("#2")) seq = "第二個";
                    else if (dow.EndsWith("#3")) seq = "第三個";
                    else if (dow.EndsWith("#4")) seq = "第四個";
                    
                    var dowMap = new System.Collections.Generic.Dictionary<string, string> {
                        {"SUN", "星期日"}, {"MON", "星期一"}, {"TUE", "星期二"}, {"WED", "星期三"},
                        {"THU", "星期四"}, {"FRI", "星期五"}, {"SAT", "星期六"}
                    };
                    string dowPrefix = dow.Substring(0, 3);
                    if (dowMap.TryGetValue(dowPrefix, out string dStr)) dowDesc = dStr;
                    
                    baseDesc = $"{mStr}的 {seq} {dowDesc} 的 {exactTime}";
                }
                else 
                {
                    baseDesc = $"自訂排程 ({trigger.CronExpression})";
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
            int repVal = trigger.RepeatInterval ?? trigger.RepeatIntervalMinutes ?? 0;
            if (repVal > 0) return "定期循環";
            return "僅一次";
        }
        var parts = trigger.CronExpression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 6) {
            var dom = parts[3]; var dow = parts[5];
            if (dom.StartsWith("1/")) return "每天";
            if ((dom == "*" || dom == "?") && (dow == "*" || dow == "?")) return "每天";
            if (dow != "*" && dow != "?") return "每週";
            if (dom == "1") return "每月";
        }
        return "自訂排程";
    }
}
