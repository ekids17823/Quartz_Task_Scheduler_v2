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
        try
        {
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=quartz.db;");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT JobGroup, JobName, ExitCode, IsSuccess, ErrorMessage FROM JobExecutionLogs ORDER BY FireTimeUtc DESC";
            using var reader = cmd.ExecuteReader();
            while(reader.Read())
            {
                string key = $"{reader.GetString(0)}::{reader.GetString(1)}";
                if (!lastRunResults.ContainsKey(key))
                {
                    bool isSuccess = reader.GetInt32(3) == 1;
                    string? errMsg = reader.IsDBNull(4) ? null : reader.GetString(4);
                    if (isSuccess)
                    {
                        int exitCode = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                        lastRunResults[key] = $"成功執行 ({exitCode})";
                    }
                    else
                    {
                        if (errMsg != null && errMsg.Contains("並發")) lastRunResults[key] = "被忽略";
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
                Author = detail.JobDataMap.ContainsKey("Author") ? detail.JobDataMap.GetString("Author") : string.Empty
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

            var jobCompositeState = "準備就緒";
            if (isRunning) jobCompositeState = "執行中";
            else if (isDisabled) jobCompositeState = "已停用";

            bool allPaused = true;
            foreach (var t in triggers)
            {
                var state = await scheduler.GetTriggerState(t.Key);
                
                string tState = state switch
                {
                    TriggerState.Normal => "準備就緒",
                    TriggerState.Paused => "已停用",
                    TriggerState.Complete => "完成",
                    TriggerState.Error => "發生錯誤",
                    TriggerState.Blocked => "執行中",
                    TriggerState.None => "準備就緒",
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
                    PreviousFireTime = t.GetPreviousFireTimeUtc()?.LocalDateTime,
                    RepeatIntervalMinutes = repeatInterval, // legacy format mapped just in case
                    RepeatInterval = repeatInterval,
                    RepeatIntervalUnit = repeatUnit,
                    WeeklyInterval = weeklyInterval,
                    State = tState
                });
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

        bool existingDisabled = false;
        if (await scheduler.CheckExists(jobKey))
        {
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
            if (tReq.EndAt.HasValue) tb.EndAt(tReq.EndAt.Value);
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
                // Just run once immediately or at StartAt
                tb.WithSimpleSchedule(x => x.WithRepeatCount(0));
            }

            triggersToSchedule.Add(tb.Build());
        }

        if (triggersToSchedule.Count == 0)
        {
             await scheduler.AddJob(job, true);
        }
        else
        {
             await scheduler.ScheduleJob(job, triggersToSchedule, replace: true);
        }

        return Ok(new { Message = "排程建立或更新成功" });
    }

    [HttpPost("{group}/{name}/trigger")]
    public async Task<IActionResult> TriggerJob(string group, string name)
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        var jobKey = new JobKey(name, group);
        if (!await scheduler.CheckExists(jobKey)) return NotFound("找不到指定的排程。");
        await scheduler.TriggerJob(jobKey);
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
        return Ok(new { Message = "排程已恢復" });
    }

    [HttpGet("{group}/{name}/logs")]
    public IActionResult GetJobLogs(string group, string name)
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=quartz.db;");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM JobExecutionLogs WHERE JobName = @JobName AND JobGroup = @JobGroup ORDER BY FireTimeUtc DESC LIMIT 50";
        cmd.Parameters.AddWithValue("@JobName", name);
        cmd.Parameters.AddWithValue("@JobGroup", group);
        
        var logs = new List<JobLogEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            logs.Add(new JobLogEntry
            {
                Id = reader.GetInt32(0),
                JobName = reader.GetString(1),
                JobGroup = reader.GetString(2),
                FireTimeUtc = reader.GetDateTime(3),
                RunTimeMs = reader.GetInt64(4),
                IsSuccess = reader.GetInt32(5) == 1,
                ExitCode = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                StdOut = reader.IsDBNull(7) ? null : reader.GetString(7),
                StdErr = reader.IsDBNull(8) ? null : reader.GetString(8),
                ErrorMessage = reader.IsDBNull(9) ? null : reader.GetString(9)
            });
        }
        return Ok(logs);
    }
}
