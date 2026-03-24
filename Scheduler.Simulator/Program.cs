using System;
using System.Collections.Generic;
using System.Linq;
using Quartz;
using Quartz.Spi;

namespace Scheduler.Simulator;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("    Quartz.NET 排程發動時機模擬測試工具 (Simulator)   ");
        Console.WriteLine("==================================================\n");

        // [設定區] ---------------------------------------------
        // 您可以在這裡隨意修改各種參數，並按下 F5 或 dotnet run 來觀看它的落點！
        
        // 1. 基底出發時間 (通常為現在，或者是您自訂的未來時間)
        DateTimeOffset startAt = DateTimeOffset.Now.Date.AddHours(10); // 取今天的早上 10:00

        // 2. 每天/每週的 Cron 表達式 (若為空字串代表 "僅一次" 單次排程)
        // 若想測試每天 10:00 執行，設為 "0 0 10 * * ?"
        // 留空 => ""
        string cronExpression = "0 0 10 * * ?"; 

        // 3. 重複間隔 (大於 0 代表啟用)
        int repeatInterval = 5;
        string repeatUnit = "Minute"; // 可填 "Minute" 或 "Hour"

        // 4. 持續時間 (大於 0 代表啟用)
        int repeatDuration = 2;
        string repeatDurationUnit = "Hour"; // 可填 "Minute", "Hour", "Day"

        // 5. 並發規則 (當上一個工作還沒跑完，下一個時間點又到了該怎麼辦)
        // 可選: "Parallel" (平行多開), "DoNothing" (忽略本次), "StopExisting" (強制切斷上一個)
        string concurrencyRule = "DoNothing"; 
        
        // 6. 全域結束時間 (可為 null)
        DateTimeOffset? absoluteEndAt = null;

        // 7. 模擬「每次工作」實際執行會耗時多久 (用來考驗並發規則)
        TimeSpan jobExecutionDuration = TimeSpan.FromMinutes(3); // 假設這支程式每次都要跑 3 分鐘

        // 8. 模擬天數 (我們往未來推演幾天的時間線？)
        int simulateDays = 2; // 推演未來 2 天內的所有觸發點
        // ----------------------------------------------------

        Console.WriteLine($"[目前參數設定]");
        Console.WriteLine($"- 排程類型: {(string.IsNullOrEmpty(cronExpression) ? "單次排程 (SimpleSchedule)" : "週期性排程 (Cron/Daily)")}");
        if (!string.IsNullOrEmpty(cronExpression)) Console.WriteLine($"- Cron表達式: {cronExpression}");
        if (repeatInterval > 0) Console.WriteLine($"- 重複執行: 每隔 {repeatInterval} {repeatUnit}");
        if (repeatDuration > 0) Console.WriteLine($"- 持續時間: {repeatDuration} {repeatDurationUnit}");
        Console.WriteLine($"- 工作耗時: {jobExecutionDuration.TotalMinutes} 分鐘");
        Console.WriteLine($"- 並發規則: {concurrencyRule}");
        Console.WriteLine($"- 模擬區間: 從 {startAt:yyyy/MM/dd HH:mm:ss} 開始算起 {simulateDays} 天內\n");

        // ==================================================================
        // 下方邏輯與 API (JobsController.cs) 轉譯 Trigger 的程式碼 100% 相同
        var tb = TriggerBuilder.Create().WithIdentity("TestTrigger");
        tb.StartAt(startAt);
        if (absoluteEndAt.HasValue) tb.EndAt(absoluteEndAt.Value);

        if (!string.IsNullOrWhiteSpace(cronExpression))
        {
            if (repeatInterval > 0)
            {
                var parts = cronExpression.Split(' ');
                if (parts.Length >= 6 && int.TryParse(parts[0], out int sec) && int.TryParse(parts[1], out int min) && int.TryParse(parts[2], out int hour))
                {
                    string dow = parts[5];
                    tb.WithDailyTimeIntervalSchedule(x => 
                    {
                        if (repeatUnit == "Hour") x.WithIntervalInHours(repeatInterval);
                        else x.WithIntervalInMinutes(repeatInterval);
                        
                        x.StartingDailyAt(new TimeOfDay(hour, min, sec));

                        if (repeatDuration > 0)
                        {
                            var ts = repeatDurationUnit == "Minute" ? TimeSpan.FromMinutes(repeatDuration) :
                                     repeatDurationUnit == "Hour" ? TimeSpan.FromHours(repeatDuration) :
                                     TimeSpan.FromDays(repeatDuration);
                            var startDt = DateTime.Today.AddHours(hour).AddMinutes(min).AddSeconds(sec);
                            var endDt = startDt.Add(ts);
                            if (endDt.Date > startDt.Date)
                                x.EndingDailyAt(new TimeOfDay(23, 59, 59));
                            else
                                x.EndingDailyAt(new TimeOfDay(endDt.Hour, endDt.Minute, endDt.Second));
                        }

                        if (dow != "?" && dow != "*") 
                        {
                            var days = new List<DayOfWeek>();
                            if (dow.Contains("SUN")) days.Add(DayOfWeek.Sunday);
                            if (dow.Contains("MON")) days.Add(DayOfWeek.Monday);
                            if (dow.Contains("TUE")) days.Add(DayOfWeek.Tuesday);
                            if (dow.Contains("WED")) days.Add(DayOfWeek.Wednesday);
                            if (dow.Contains("THU")) days.Add(DayOfWeek.Thursday);
                            if (dow.Contains("FRI")) days.Add(DayOfWeek.Friday);
                            if (dow.Contains("SAT")) days.Add(DayOfWeek.Saturday);
                            if (days.Count > 0) x.OnDaysOfTheWeek(days.ToArray());
                            else x.OnEveryDay();
                        } 
                        else 
                        {
                            x.OnEveryDay();
                        }
                    });
                }
                else
                {
                    tb.WithCronSchedule(cronExpression);
                }
            }
            else
            {
                tb.WithCronSchedule(cronExpression);
            }
        }
        else if (repeatInterval > 0)
        {
            if (repeatDuration > 0)
            {
                var ts = repeatDurationUnit == "Minute" ? TimeSpan.FromMinutes(repeatDuration) :
                         repeatDurationUnit == "Hour" ? TimeSpan.FromHours(repeatDuration) :
                         TimeSpan.FromDays(repeatDuration);
                tb.EndAt(startAt.Add(ts));
            }
            tb.WithSimpleSchedule(x => 
            {
                if (repeatUnit == "Hour") x.WithIntervalInHours(repeatInterval).RepeatForever();
                else x.WithIntervalInMinutes(repeatInterval).RepeatForever();
            });
        }
        else
        {
            tb.WithSimpleSchedule(x => x.WithRepeatCount(0));
        }

        var trigger = tb.Build();

        // ==================================================================
        // 提取未來 N 天的原始觸發時間 (先不管執行耗時，純看 Quartz 本身的自然排程落點)
        var toTime = startAt.AddDays(simulateDays);
        // 呼叫 Quartz 底層核心
        var fireTimes = TriggerUtils.ComputeFireTimes((IOperableTrigger)trigger, null, 10000)
                            .Where(t => t <= toTime && t >= startAt)
                            .ToList();

        if (fireTimes.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\n⚠️ 警告：在給定的時間內，沒有任何觸發落點！請檢查 StartAt / EndAt 是否設定有誤，或已經過期。\n");
            Console.ResetColor();
            return;
        }

        Console.WriteLine($"=> ✅ Quartz 共推算出 {fireTimes.Count} 個預定觸發點。\n");
        Console.WriteLine("============= [時間軸情境推演] =============");

        DateTimeOffset? currentJobEndTime = null;
        int executedCount = 0;
        int ignoredCount = 0;

        foreach (var fireTime in fireTimes)
        {
            Console.Write($"[{fireTime:MM/dd HH:mm:ss}] - 觸發信號抵達: ");

            // 判別並發衝突
            if (currentJobEndTime.HasValue && fireTime < currentJobEndTime.Value)
            {
                // 發現衝突：上一個工作還沒跑完，下一個觸發點又到了
                if (concurrencyRule == "DoNothing")
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("依並發規則略過 (因為上一輪的程序仍在執行中)");
                    Console.ResetColor();
                    ignoredCount++;
                    continue; // 直接放棄這次排程，什麼都不做
                }
                else if (concurrencyRule == "StopExisting")
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"強行中止前次執行，並啟動新一輪任務 (預計跑到 {fireTime.Add(jobExecutionDuration):MM/dd HH:mm:ss})");
                    Console.ResetColor();
                    currentJobEndTime = fireTime.Add(jobExecutionDuration);
                    executedCount++;
                }
                else // Parallel (平行)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"平行啟動新任務，新舊雙開 (新任務預計跑到 {fireTime.Add(jobExecutionDuration):MM/dd HH:mm:ss})");
                    Console.ResetColor();
                    // 平行模式下互不干擾，模擬器記錄兩者中最晚結束的時間即可
                    currentJobEndTime = fireTime.Add(jobExecutionDuration) > currentJobEndTime.Value 
                        ? fireTime.Add(jobExecutionDuration) 
                        : currentJobEndTime.Value;
                    executedCount++;
                }
            }
            else
            {
                // 無衝突，正常啟動
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"正常啟動 (預計執行 3 分鐘至 {fireTime.Add(jobExecutionDuration):HH:mm:ss} 結束)");
                Console.ResetColor();
                currentJobEndTime = fireTime.Add(jobExecutionDuration);
                executedCount++;
            }
        }

        Console.WriteLine("============================================");
        Console.WriteLine($"\n=== [模擬總結] ===");
        Console.WriteLine($"時間推進： {simulateDays} 天");
        Console.WriteLine($"觸發信號： 總共 {fireTimes.Count} 次");
        Console.WriteLine($"真實發動： {executedCount} 次");
        Console.WriteLine($"略過阻擋： {ignoredCount} 次\n");
        Console.WriteLine("[小提示] 若想測試不同情境，請回到 Program.cs 修改最上方的參數設定區。\n");
    }
}
