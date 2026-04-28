using Microsoft.AspNetCore.Mvc;
using Quartz;
using Scheduler.Api.Services;
using Scheduler.Core.Jobs;
using Scheduler.Core.Models;
using Scheduler.Core.Constants;
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
    private readonly IJobQueryService _jobQueryService;
    private readonly IJobLogQueryService _jobLogQueryService;
    private readonly IAuditLogQueryService _auditLogQueryService;

    public JobsController(
        ISchedulerFactory schedulerFactory,
        ITriggerFactory triggerFactory,
        IAuditLogService auditLogService,
        IJobQueryService jobQueryService,
        IJobLogQueryService jobLogQueryService,
        IAuditLogQueryService auditLogQueryService)
    {
        _schedulerFactory = schedulerFactory;
        _triggerFactory = triggerFactory;
        _auditLogService = auditLogService;
        _jobQueryService = jobQueryService;
        _jobLogQueryService = jobLogQueryService;
        _auditLogQueryService = auditLogQueryService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllJobs()
    {
        return Ok(await _jobQueryService.GetAllJobsAsync());
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

        await _auditLogService.SaveAsync(isUpdate ? SchedulerEventIds.JobUpdated : SchedulerEventIds.JobCreated, request.JobName, request.JobGroup, isUpdate ? "更新了排程任務的設定與觸發條件。" : "建立了新的排程任務。");

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
        await _auditLogService.SaveAsync(SchedulerEventIds.JobDeleted, name, group, "刪除了排程任務。");
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
        await _auditLogService.SaveAsync(SchedulerEventIds.JobPaused, name, group, "停用了排程任務。");
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
        await _auditLogService.SaveAsync(SchedulerEventIds.JobResumed, name, group, "啟用了排程任務。");
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
    public async Task<IActionResult> GetJobLogs(string group, string name)
    {
        return Ok(await _jobLogQueryService.GetJobLogsAsync(group, name));
    }

    [HttpGet("auditlogs")]
    public async Task<IActionResult> GetAuditLogs()
    {
        return Ok(await _auditLogQueryService.GetAuditLogsAsync());
    }

}
