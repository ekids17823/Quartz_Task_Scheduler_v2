using Microsoft.AspNetCore.Mvc;
using Quartz;
using Scheduler.Core.Jobs;
using Scheduler.Core.Models;
using Scheduler.Core.Services;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Scheduler.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly ITriggerFactory _triggerFactory;
    private readonly IAuditLogService _auditLogService;

    public JobsController(ISchedulerFactory schedulerFactory, ITriggerFactory triggerFactory, IAuditLogService auditLogService)
    {
        _schedulerFactory = schedulerFactory;
        _triggerFactory = triggerFactory;
        _auditLogService = auditLogService;
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
            cmd.CommandText = "SELECT JobGroup, JobName, ExitCode, IsSuccess, ErrorMessage, FireTimeUtc, EventId FROM JobExecutionLogs ORDER BY FireTimeUtc DESC, Id DESC";
            using var reader = cmd.ExecuteReader();
            while(reader.Read())
            {
                string key = $"{reader.GetString(0)}::{reader.GetString(1)}";
                int eventId = reader.GetInt32(6);

                if (!lastRunResults.ContainsKey(key))
                {
                    bool isSuccess = reader.GetInt32(3) == 1;
                    string? errMsg = reader.IsDBNull(4) ? null : reader.GetString(4);

                    if (eventId == 201)
                    {
                        int exitCode = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                        lastRunResults[key] = $"成功執行 ({exitCode})";
                    }
                    else if (eventId == 203 || (!isSuccess && errMsg != null && !errMsg.Contains("並發") && !errMsg.Contains("中斷")))
                    {
                        lastRunResults[key] = "執行失敗";
                    }
                    else if (eventId == 322 || (errMsg != null && errMsg.Contains("並發")))
                    {
                        lastRunResults[key] = "依並發規則略過";
                    }
                    else if (eventId == 323 || (errMsg != null && errMsg.Contains("週規則")))
                    {
                        lastRunResults[key] = "依每週間隔規則略過";
                    }
                    else if (eventId == 328 || (errMsg != null && errMsg.Contains("中斷")))
                    {
                        lastRunResults[key] = "已終止";
                    }
                    // 100, 107, 110, 129, 200 為過程事件，不設定結果以尋找上一筆結案
                }

                if (!lastRunTimes.ContainsKey(key) && (eventId == 107 || eventId == 110))
                {
                    lastRunTimes[key] = reader.GetDateTime(5);
                }
            }
        }
        catch { }


        foreach (var jobKey in jobKeys)
        {
            if (jobKey.Name == "System_LogCleanup") continue; // 隱藏系統清理排程
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
            var builtTrigger = _triggerFactory.BuildForCreate(jobKey, tReq, request.MisfireActionFireAndProceed);
            if (builtTrigger != null)
            {
                triggersToSchedule.Add(builtTrigger);
            }
        }

        if (existingDisabled)
        {
             await scheduler.AddJob(job, true);
        }
        else if (triggersToSchedule.Count == 0)
        {
             var existingTriggers = await scheduler.GetTriggersOfJob(job.Key);
             if (existingTriggers.Any())
             {
                 await scheduler.UnscheduleJobs(existingTriggers.Select(t => t.Key).ToList());
             }
             await scheduler.AddJob(job, true);
        }
        else
        {
             await scheduler.ScheduleJob(job, triggersToSchedule, replace: true);
        }

        await _auditLogService.SaveAsync(isUpdate ? 140 : 106, request.JobName, request.JobGroup, isUpdate ? "更新了排程任務的設定與觸發條件。" : "建立了新的排程任務。");

        return Ok(new { Message = "排程建立或更新成功" });
    }

    [HttpPost("{group}/{name}/trigger")]
    public async Task<IActionResult> TriggerJob(string group, string name)
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        var jobKey = new JobKey(name, group);
        if (!await scheduler.CheckExists(jobKey)) return NotFound("找不到指定的排程。");
        if (await IsJobDisabledAsync(scheduler, jobKey))
        {
            return Conflict("排程已停用，不能觸發。請先啟用排程。");
        }

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
        await _auditLogService.SaveAsync(141, name, group, "刪除了排程任務。");
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
        await _auditLogService.SaveAsync(142, name, group, "停用了排程任務。");
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

        await RebuildTriggersForResumeAsync(scheduler, jobKey, detail);
        await _auditLogService.SaveAsync(143, name, group, "啟用了排程任務。");
        return Ok(new { Message = "排程已恢復" });
    }

    private static async Task<bool> IsJobDisabledAsync(IScheduler scheduler, JobKey jobKey)
    {
        var detail = await scheduler.GetJobDetail(jobKey);
        return detail?.JobDataMap.ContainsKey("IsDisabled") == true
            && bool.TryParse(detail.JobDataMap.GetString("IsDisabled"), out var disabled)
            && disabled;
    }

    private async Task RebuildTriggersForResumeAsync(IScheduler scheduler, JobKey jobKey, IJobDetail? detail)
    {
        detail ??= await scheduler.GetJobDetail(jobKey);
        if (detail == null)
        {
            return;
        }

        var currentTriggers = await scheduler.GetTriggersOfJob(jobKey);
        if (currentTriggers.Any())
        {
            await scheduler.UnscheduleJobs(currentTriggers.Select(t => t.Key).ToList());
        }

        List<TriggerDto> originalTriggers = new();
        if (detail.JobDataMap.ContainsKey("OriginalTriggers"))
        {
            try
            {
                originalTriggers = System.Text.Json.JsonSerializer.Deserialize<List<TriggerDto>>(
                    detail.JobDataMap.GetString("OriginalTriggers") ?? string.Empty) ?? new();
            }
            catch
            {
                originalTriggers = new();
            }
        }

        if (originalTriggers.Count == 0)
        {
            await scheduler.ResumeJob(jobKey);
            return;
        }

        bool misfireActionFireAndProceed = detail.JobDataMap.ContainsKey("MisfireActionFireAndProceed")
            && bool.TryParse(detail.JobDataMap.GetString("MisfireActionFireAndProceed"), out var misfire)
            && misfire;

        var rebuiltTriggers = new HashSet<ITrigger>();
        foreach (var triggerDto in originalTriggers)
        {
            var trigger = _triggerFactory.BuildForResume(jobKey, triggerDto, misfireActionFireAndProceed);
            if (trigger != null)
            {
                rebuiltTriggers.Add(trigger);
            }
        }

        if (rebuiltTriggers.Count > 0)
        {
            await scheduler.ScheduleJob(detail, rebuiltTriggers, replace: true);
        }
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
                eventId = isSuccess ? 201 : (errMsg != null && errMsg.Contains("強制中斷") ? 328 : (errMsg != null && errMsg.Contains("並發") ? 322 : (errMsg != null && errMsg.Contains("週規則") ? 323 : 203)));
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

}
