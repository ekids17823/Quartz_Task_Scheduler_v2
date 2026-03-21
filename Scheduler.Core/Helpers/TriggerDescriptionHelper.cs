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
                        baseDesc = $"從 {start} 開始，每個星期的 {dowStr} 的 {exactTime}";
                    }
                }
                else if (dom == "1") // 簡易每月
                {
                    baseDesc = $"每個月的 第一天 的 {exactTime}";
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

        if (trigger.RepeatIntervalMinutes.HasValue && trigger.RepeatIntervalMinutes.Value > 0)
        {
            baseDesc += $" - 觸發之後，每 {trigger.RepeatIntervalMinutes.Value} 分鐘便重複一次。";
        }

        return baseDesc;
    }

    public static string GetTriggerType(TriggerDto trigger)
    {
        if (string.IsNullOrWhiteSpace(trigger.CronExpression)) return "僅一次";
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
