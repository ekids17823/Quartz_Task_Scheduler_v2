using Microsoft.AspNetCore.Mvc;
using Quartz;
using Scheduler.Core.Jobs;
using Scheduler.Core.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;

namespace Scheduler.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly ISchedulerFactory _schedulerFactory;

    public JobsController(ISchedulerFactory schedulerFactory)
    {
        _schedulerFactory = schedulerFactory;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllJobs()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        var jobKeys = await scheduler.GetJobKeys(global::Quartz.Impl.Matchers.GroupMatcher<JobKey>.AnyGroup());
        var executingJobs = await scheduler.GetCurrentlyExecutingJobs();
        var jobs = new List<JobInfo>();

        var lastRunResults = new Dictionary<string, string>();
        var lastRunTimes = new Dictionary<string, DateTime>();
        try
        {
            string dbPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "quartz.db");
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT JobGroup, JobName, ExitCode, IsSuccess, ErrorMessage, FireTimeUtc FROM JobExecutionLogs ORDER BY FireTimeUtc DESC, Id DESC";
            using var reader = cmd.ExecuteReader();
            while(reader.Read())
            {
                string key = $"{reader.GetString(0)}::{reader.GetString(1)}";
                if (!lastRunResults.ContainsKey(key))
                {
                    lastRunTimes[key] = reader.GetDateTime(5);
                    bool isSuccess = reader.GetInt32(3) == 1;
                    string? errMsg = reader.IsDBNull(4) ? null : reader.GetString(4);
                    if (isSuccess)
                    {
                        int exitCode = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                        lastRunResults[key] = $"成功執行 ({exitCode})";
                    }
                    else
                    {
                        if (errMsg != null && errMsg.Contains("並發")) lastRunResults[key] = "依並發規則略過";
                        else if (errMsg != null && errMsg.Contains("中斷")) lastRunResults[key] = "已終止";
                        else lastRunResults[key] = "執行失敗";
                    }
                }
            }
        }
        catch { }


        foreach (var jobKey in jobKeys)
        {
            var detail = await scheduler.GetJobDetail(jobKey);
            if (detail == null) continue;
            
            var triggers = await scheduler.GetTriggersOfJob(jobKey);
            bool isRunning = executingJobs.Any(x => x.JobDetail.Key.Equals(jobKey));

            var jobInfo = new JobInfo
            {
                JobName = jobKey.Name,
                JobGroup = jobKey.Group,
                Description = detail.Description,
                FileName = detail.JobDataMap.ContainsKey("FileName") ? detail.JobDataMap.GetString("FileName") : null,
                Arguments = detail.JobDataMap.ContainsKey("Arguments") ? detail.JobDataMap.GetString("Arguments") : null,
                WorkingDirectory = detail.JobDataMap.ContainsKey("WorkingDirectory") ? detail.JobDataMap.GetString("WorkingDirectory") : null,
                Author = detail.JobDataMap.ContainsKey("Author") ? (detail.JobDataMap.GetString("Author") ?? string.Empty) : string.Empty
            };
            
            if (detail.JobDataMap.ContainsKey("MisfireActionFireAndProceed"))
            {
                if (bool.TryParse(detail.JobDataMap.GetString("MisfireActionFireAndProceed"), out var m)) jobInfo.MisfireActionFireAndProceed = m;
            }
            if (detail.JobDataMap.ContainsKey("ConcurrencyRule"))
            {
                jobInfo.ConcurrencyRule = detail.JobDataMap.GetString("ConcurrencyRule") ?? "Parallel";
            }
            if (detail.JobDataMap.ContainsKey("IsHidden") && bool.TryParse(detail.JobDataMap.GetString("IsHidden"), out var h))
            {
                jobInfo.IsHidden = h;
            }
            
            if (detail.JobDataMap.ContainsKey("MaxRunTimeSeconds"))
            {
                var maxRuntimeStr = detail.JobDataMap.GetString("MaxRunTimeSeconds");
                if (int.TryParse(maxRuntimeStr, out var maxRunTime)) 
                    jobInfo.MaxRunTimeSeconds = maxRunTime;
            }

            bool isDisabled = false;
            if (detail.JobDataMap.ContainsKey("IsDisabled") && bool.TryParse(detail.JobDataMap.GetString("IsDisabled"), out var d))
            {
                isDisabled = d;
            }

            var jobCompositeState = "就緒";
            if (isRunning) jobCompositeState = "執行中";
            else if (isDisabled) jobCompositeState = "已停用";

            DateTime? jobLastRunTime = lastRunTimes.TryGetValue($"{jobKey.Group}::{jobKey.Name}", out var outDt) ? outDt.ToLocalTime() : null;

            List<TriggerDto> originalTriggers = new();
            if (detail.JobDataMap.ContainsKey("OriginalTriggers"))
            {
                try { originalTriggers = System.Text.Json.JsonSerializer.Deserialize<List<TriggerDto>>(detail.JobDataMap.GetString("OriginalTriggers")!) ?? new(); } catch { }
            }

            if (originalTriggers.Count > 0)
            {
                foreach(var origT in originalTriggers)
                {
                    origT.PreviousFireTime = jobLastRunTime;
                    var liveT = triggers.FirstOrDefault(x => x.Key.Name == origT.TriggerName && x.Key.Group == origT.TriggerGroup);
                    if (liveT != null)
                    {
                        var state = await scheduler.GetTriggerState(liveT.Key);
                        origT.State = state switch
                        {
                            TriggerState.Normal => "就緒", TriggerState.Paused => "已停用", 
                            TriggerState.Complete => "完成", TriggerState.Error => "發生錯誤",
                            TriggerState.Blocked => "執行中", TriggerState.None => "就緒", _ => state.ToString()
                        };
                        origT.NextFireTime = liveT.GetNextFireTimeUtc()?.LocalDateTime;
                    }
                    else
                    {
                        origT.State = "已完成/過期";
                        origT.NextFireTime = null;
                        // origT properties like StartAt/EndAt remain exactly as user configured them
                    }
                    jobInfo.Triggers.Add(origT);
                }
            }
            else
            {
                foreach (var t in triggers)
                {
                    var state = await scheduler.GetTriggerState(t.Key);
                string tState = state switch
                {
                    TriggerState.Normal => "就緒",
                    TriggerState.Paused => "已停用",
                    TriggerState.Complete => "完成",
                    TriggerState.Error => "發生錯誤",
                    TriggerState.Blocked => "執行中",
                    TriggerState.None => "就緒",
                    _ => state.ToString()
                };

                string? cronEx = (t as ICronTrigger)?.CronExpressionString;

                if (t is IDailyTimeIntervalTrigger dt)
                {
                    int sec = dt.StartTimeOfDay.Second;
                    int min = dt.StartTimeOfDay.Minute;
                    int hour = dt.StartTimeOfDay.Hour;
                    
                    if (dt.DaysOfWeek.Count == 7)
                    {
                        cronEx = $"{sec} {min} {hour} * * ?"; // Daily
                    }
                    else if (dt.DaysOfWeek.Count > 0)
                    {
                        var daysMap = new Dictionary<DayOfWeek, string> {
                            {DayOfWeek.Sunday, "SUN"}, {DayOfWeek.Monday, "MON"}, {DayOfWeek.Tuesday, "TUE"},
                            {DayOfWeek.Wednesday, "WED"}, {DayOfWeek.Thursday, "THU"}, {DayOfWeek.Friday, "FRI"}, {DayOfWeek.Saturday, "SAT"}
                        };
                        var dowList = dt.DaysOfWeek.Select(d => daysMap[d]);
                        cronEx = $"{sec} {min} {hour} ? * {string.Join(",", dowList)}";
                    }
                }
                
                int? repeatInterval = null;
                string? repeatUnit = null;
                if (t.JobDataMap.ContainsKey("RepeatInterval")) {
                    if (int.TryParse(t.JobDataMap.GetString("RepeatInterval"), out int val)) repeatInterval = val;
                    repeatUnit = t.JobDataMap.GetString("RepeatIntervalUnit") ?? "Minute";
                } else if (t.JobDataMap.ContainsKey("RepeatIntervalMinutes")) {
                    if (int.TryParse(t.JobDataMap.GetString("RepeatIntervalMinutes"), out int val)) { repeatInterval = val; repeatUnit = "Minute"; }
                }
                int? weeklyInterval = null;
                if (t.JobDataMap.ContainsKey("WeeklyInterval"))
                {
                    if (int.TryParse(t.JobDataMap.GetString("WeeklyInterval"), out int val)) weeklyInterval = val;
                }
                
                jobInfo.Triggers.Add(new TriggerDto
                {
                    TriggerName = t.Key.Name,
                    TriggerGroup = t.Key.Group,
                    Description = t.Description ?? string.Empty,
                    CronExpression = cronEx,
                    StartAt = t.StartTimeUtc,
                    EndAt = t.EndTimeUtc,
                    NextFireTime = t.GetNextFireTimeUtc()?.LocalDateTime,
                    PreviousFireTime = jobLastRunTime,
                    RepeatIntervalMinutes = repeatInterval, // legacy format mapped just in case
                    RepeatInterval = repeatInterval,
                    RepeatIntervalUnit = repeatUnit,
                    WeeklyInterval = weeklyInterval,
                    State = tState
                });
            }
            }

            jobInfo.State = jobCompositeState;
            string dicKey = $"{jobKey.Group}::{jobKey.Name}";
            jobInfo.LastRunResult = lastRunResults.ContainsKey(dicKey) ? lastRunResults[dicKey] : null;
            jobs.Add(jobInfo);
        }

        return Ok(jobs);
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrUpdateJob([FromBody] ScheduleRequest request)
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        var jobKey = new JobKey(request.JobName, request.JobGroup);

        bool isUpdate = false;
        bool existingDisabled = false;
        if (await scheduler.CheckExists(jobKey))
        {
            isUpdate = true;
            var oldDetail = await scheduler.GetJobDetail(jobKey);
            if (oldDetail != null && oldDetail.JobDataMap.ContainsKey("IsDisabled"))
            {
                if (bool.TryParse(oldDetail.JobDataMap.GetString("IsDisabled"), out var d)) existingDisabled = d;
            }
            await scheduler.DeleteJob(jobKey);
        }

        var jobBuilder = JobBuilder.Create<ProcessRunnerJob>()
            .WithIdentity(jobKey)
            .WithDescription(request.Description)
            .UsingJobData("FileName", request.FileName)
            .UsingJobData("Arguments", request.Arguments)
            .UsingJobData("WorkingDirectory", request.WorkingDirectory)
            .UsingJobData("MisfireActionFireAndProceed", request.MisfireActionFireAndProceed.ToString())
            .UsingJobData("ConcurrencyRule", request.ConcurrencyRule)
            .UsingJobData("IsDisabled", existingDisabled.ToString())
            .UsingJobData("IsHidden", request.IsHidden.ToString())
            .UsingJobData("Author", request.Author)
            .UsingJobData("OriginalTriggers", System.Text.Json.JsonSerializer.Serialize(request.Triggers))
            .StoreDurably();

        if (request.MaxRunTimeSeconds.HasValue)
        {
            jobBuilder.UsingJobData("MaxRunTimeSeconds", request.MaxRunTimeSeconds.Value.ToString());
        }

        var job = jobBuilder.Build();
        var triggersToSchedule = new HashSet<ITrigger>();

        foreach (var tReq in request.Triggers)
        {
            var triggerKey = new TriggerKey(tReq.TriggerName, tReq.TriggerGroup);
            var tb = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .ForJob(job)
                .WithDescription(tReq.Description);

            if (tReq.StartAt.HasValue) tb.StartAt(tReq.StartAt.Value);
            
            if (tReq.EndAt.HasValue) 
            {
                tb.EndAt(tReq.EndAt.Value);
            }
            else if (string.IsNullOrWhiteSpace(tReq.CronExpression) && tReq.RepeatDuration.HasValue && tReq.RepeatDuration.Value > 0)
            {
                DateTimeOffset baseStart = tReq.StartAt ?? DateTimeOffset.UtcNow;
                string durUnit = tReq.RepeatDurationUnit ?? "Minute";
                var ts = durUnit == "Minute" ? TimeSpan.FromMinutes(tReq.RepeatDuration.Value) :
                         durUnit == "Hour" ? TimeSpan.FromHours(tReq.RepeatDuration.Value) :
                         TimeSpan.FromDays(tReq.RepeatDuration.Value);
                tb.EndAt(baseStart.Add(ts));
            }
            int repVal = tReq.RepeatInterval ?? tReq.RepeatIntervalMinutes ?? 0;
            string repUnit = string.IsNullOrWhiteSpace(tReq.RepeatIntervalUnit) ? "Minute" : tReq.RepeatIntervalUnit;
            if (repVal > 0)
            {
                tb.UsingJobData("RepeatInterval", repVal.ToString());
                tb.UsingJobData("RepeatIntervalUnit", repUnit);
            }
            if (tReq.WeeklyInterval.HasValue && tReq.WeeklyInterval.Value > 1)
            {
                tb.UsingJobData("WeeklyInterval", tReq.WeeklyInterval.Value.ToString());
            }

            if (!string.IsNullOrWhiteSpace(tReq.CronExpression))
            {
                // 若進階設定啟用了「每 N 分鐘重複一次」，且本身是每天/每週的 Cron，我們必須轉譯為 DailyTimeIntervalSchedule
                if (repVal > 0)
                {
                    var parts = tReq.CronExpression.Split(' ');
                    // 確保這是由我們 ViewModel 產生、可預測的 Cron (長度 >= 6 且包含具體時分)
                    if (parts.Length >= 6 && int.TryParse(parts[0], out int sec) && int.TryParse(parts[1], out int min) && int.TryParse(parts[2], out int hour))
                    {
                        string dow = parts[5];
                        tb.WithDailyTimeIntervalSchedule(x => 
                        {
                            if (repUnit == "Second") x.WithIntervalInSeconds(repVal);
                            else if (repUnit == "Hour") x.WithIntervalInHours(repVal);
                            else x.WithIntervalInMinutes(repVal);
                            
                            x.StartingDailyAt(new TimeOfDay(hour, min, sec));

                            if (tReq.RepeatDuration.HasValue && tReq.RepeatDuration.Value > 0)
                            {
                                string dUnit = tReq.RepeatDurationUnit ?? "Minute";
                                var ts = dUnit == "Minute" ? TimeSpan.FromMinutes(tReq.RepeatDuration.Value) :
                                         dUnit == "Hour" ? TimeSpan.FromHours(tReq.RepeatDuration.Value) :
                                         TimeSpan.FromDays(tReq.RepeatDuration.Value);
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

                            if (request.MisfireActionFireAndProceed) x.WithMisfireHandlingInstructionFireAndProceed();
                            else x.WithMisfireHandlingInstructionDoNothing();
                        });
                    }
                    else
                    {
                        // 無法轉譯 (例如每月執行)，回退純 Cron
                        tb.WithCronSchedule(tReq.CronExpression, x => 
                        {
                            if (request.MisfireActionFireAndProceed) x.WithMisfireHandlingInstructionFireAndProceed();
                            else x.WithMisfireHandlingInstructionDoNothing();
                        });
                    }
                }
                else
                {
                    tb.WithCronSchedule(tReq.CronExpression, x => 
                    {
                        if (request.MisfireActionFireAndProceed) x.WithMisfireHandlingInstructionFireAndProceed();
                        else x.WithMisfireHandlingInstructionDoNothing();
                    });
                }
            }
            else if (repVal > 0)
            {
                tb.WithSimpleSchedule(x => 
                {
                    if (repUnit == "Second") x.WithIntervalInSeconds(repVal).RepeatForever();
                    else if (repUnit == "Hour") x.WithIntervalInHours(repVal).RepeatForever();
                    else x.WithIntervalInMinutes(repVal).RepeatForever();
                    if (request.MisfireActionFireAndProceed) x.WithMisfireHandlingInstructionFireNow();
                    else x.WithMisfireHandlingInstructionNextWithExistingCount();
                });
            }
            else
            {
                // 單次執行：判斷若時間已成過去式，且使用者未勾選「補跑」，乾脆不交給引擎排程！
                if (!request.MisfireActionFireAndProceed && tReq.StartAt.HasValue && tReq.StartAt.Value < DateTimeOffset.UtcNow)
                {
                    continue; // 直接跳過，不加入 triggersToSchedule，從根源截斷預設觸發！
                }

                // 反之，若時間在未來，或是使用者希望它盡快執行，則排定送出
                tb.WithSimpleSchedule(x => 
                {
                    x.WithRepeatCount(0);
                    if (request.MisfireActionFireAndProceed) x.WithMisfireHandlingInstructionFireNow();
                });
            }

            var builtTrigger = tb.Build();

            // 若為循環任務，且使用者未勾選「補跑」，我們直接精算未來的真實起跑點
            if (!request.MisfireActionFireAndProceed)
            {
                var triggerStartTime = builtTrigger.StartTimeUtc;
                // 若引擎判定目前的起跑點是在過去或剛好是「現在」，為了避免存檔馬上第一發觸發
                if (triggerStartTime <= DateTimeOffset.UtcNow)
                {
                    var nextFire = builtTrigger.GetFireTimeAfter(DateTimeOffset.UtcNow);
                    if (nextFire.HasValue)
                    {
                        // 覆寫啟動時間為純未來的下一個正確節點，讓它安靜等待到那刻
                        tb.StartAt(nextFire.Value);
                        builtTrigger = tb.Build();
                    }
                }
            }

            triggersToSchedule.Add(builtTrigger);
        }

        if (triggersToSchedule.Count == 0)
        {
             await scheduler.AddJob(job, true);
        }
        else
        {
             await scheduler.ScheduleJob(job, triggersToSchedule, replace: true);
        }

        SaveAuditLog(isUpdate ? 140 : 106, request.JobName, request.JobGroup, isUpdate ? "更新了排程任務的設定與觸發條件。" : "建立了新的排程任務。");

        return Ok(new { Message = "排程建立或更新成功" });
    }

    [HttpPost("{group}/{name}/trigger")]
    public async Task<IActionResult> TriggerJob(string group, string name)
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        var jobKey = new JobKey(name, group);
        if (!await scheduler.CheckExists(jobKey)) return NotFound("找不到指定的排程。");
        await scheduler.TriggerJob(jobKey, new global::Quartz.JobDataMap { { "TriggerReason", "Manual" } });
        return Ok(new { Message = "已觸發。" });
    }

    [HttpPost("{group}/{name}/interrupt")]
    public async Task<IActionResult> InterruptJob(string group, string name)
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        var jobKey = new JobKey(name, group);
        if (!await scheduler.CheckExists(jobKey)) return NotFound();
        bool interrupted = await scheduler.Interrupt(jobKey);
        return Ok(new { Message = interrupted ? "已傳送結束訊號。" : "無法結束，工作可能並未執行。" });
    }

    [HttpDelete("{group}/{name}")]
    public async Task<IActionResult> DeleteJob(string group, string name)
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        var jobKey = new JobKey(name, group);
        if (!await scheduler.CheckExists(jobKey)) return NotFound();
        await scheduler.DeleteJob(jobKey);
        SaveAuditLog(141, name, group, "刪除了排程任務。");
        return Ok(new { Message = "排程刪除成功" });
    }

    [HttpPost("{group}/{name}/pause")]
    public async Task<IActionResult> PauseJob(string group, string name)
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        var jobKey = new JobKey(name, group);
        if (!await scheduler.CheckExists(jobKey)) return NotFound();

        var detail = await scheduler.GetJobDetail(jobKey);
        if (detail != null)
        {
            detail.JobDataMap.Put("IsDisabled", "True");
            await scheduler.AddJob(detail, true, true);
        }

        await scheduler.PauseJob(jobKey);
        SaveAuditLog(142, name, group, "停用了排程任務。");
        return Ok(new { Message = "排程已暫停" });
    }

    [HttpPost("{group}/{name}/resume")]
    public async Task<IActionResult> ResumeJob(string group, string name)
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        var jobKey = new JobKey(name, group);
        if (!await scheduler.CheckExists(jobKey)) return NotFound();

        var detail = await scheduler.GetJobDetail(jobKey);
        if (detail != null)
        {
            detail.JobDataMap.Put("IsDisabled", "False");
            await scheduler.AddJob(detail, true, true);
        }

        await scheduler.ResumeJob(jobKey);
        SaveAuditLog(143, name, group, "啟用了排程任務。");
        return Ok(new { Message = "排程已恢復" });
    }

    [HttpGet("{group}/{name}/logs")]
    public IActionResult GetJobLogs(string group, string name)
    {
        string dbPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "quartz.db");
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM JobExecutionLogs WHERE JobName = @JobName AND JobGroup = @JobGroup AND FireTimeUtc >= datetime('now', '-7 days') ORDER BY FireTimeUtc DESC, Id DESC LIMIT 2000";
        cmd.Parameters.AddWithValue("@JobName", name);
        cmd.Parameters.AddWithValue("@JobGroup", group);
        
        var logs = new List<JobLogEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            int eventId = reader.FieldCount > 11 && !reader.IsDBNull(11) ? reader.GetInt32(11) : 0;
            bool isSuccess = reader.GetInt32(5) == 1;
            string? errMsg = reader.IsDBNull(9) ? null : reader.GetString(9);
            
            // 舊版資料安全相容性映射
            if (eventId == 0)
            {
                eventId = isSuccess ? 201 : (errMsg != null && errMsg.Contains("強制中斷") ? 328 : (errMsg != null && errMsg.Contains("並發") ? 322 : 203));
            }
            
            logs.Add(new JobLogEntry
            {
                Id = reader.GetInt32(0),
                JobName = reader.GetString(1),
                JobGroup = reader.GetString(2),
                FireTimeUtc = reader.GetDateTime(3),
                RunTimeMs = reader.GetInt64(4),
                IsSuccess = isSuccess,
                ExitCode = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                StdOut = reader.IsDBNull(7) ? null : reader.GetString(7),
                StdErr = reader.IsDBNull(8) ? null : reader.GetString(8),
                ErrorMessage = errMsg,
                CorrelationId = reader.FieldCount > 10 && !reader.IsDBNull(10) ? reader.GetString(10) : string.Empty,
                EventId = eventId
            });
        }
        return Ok(logs);
    }

    [HttpGet("auditlogs")]
    public IActionResult GetAuditLogs()
    {
        string dbPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "quartz.db");
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, EventId, EventTimeUtc, JobName, JobGroup, Description, AccountName FROM AuditLogs ORDER BY EventTimeUtc DESC, Id DESC LIMIT 1000";
        var logs = new List<AuditLogEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            logs.Add(new AuditLogEntry
            {
                Id = reader.GetInt32(0),
                EventId = reader.GetInt32(1),
                EventTimeUtc = reader.GetDateTime(2),
                JobName = reader.GetString(3),
                JobGroup = reader.GetString(4),
                Description = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                AccountName = reader.IsDBNull(6) ? string.Empty : reader.GetString(6)
            });
        }
        return Ok(logs);
    }

    private void SaveAuditLog(int eventId, string jobName, string jobGroup, string description)
    {
        try
        {
            string dbPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "quartz.db");
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO AuditLogs (EventId, EventTimeUtc, JobName, JobGroup, Description, AccountName) 
                VALUES (@EventId, @EventTimeUtc, @JobName, @JobGroup, @Description, @AccountName)";
            cmd.Parameters.AddWithValue("@EventId", eventId);
            cmd.Parameters.AddWithValue("@EventTimeUtc", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@JobName", jobName);
            cmd.Parameters.AddWithValue("@JobGroup", jobGroup);
            cmd.Parameters.AddWithValue("@Description", description);
            cmd.Parameters.AddWithValue("@AccountName", Environment.UserDomainName + "\\" + Environment.UserName);
            cmd.ExecuteNonQuery();
        }
        catch { }
    }
}
