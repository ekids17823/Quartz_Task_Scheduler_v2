using Quartz;
using Scheduler.Core.Models;

namespace Scheduler.Api.Services;

public interface IJobQueryService
{
    Task<List<JobInfo>> GetAllJobsAsync();
}

public class JobQueryService : IJobQueryService
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly IJobLogQueryService _jobLogQueryService;

    public JobQueryService(ISchedulerFactory schedulerFactory, IJobLogQueryService jobLogQueryService)
    {
        _schedulerFactory = schedulerFactory;
        _jobLogQueryService = jobLogQueryService;
    }

    public async Task<List<JobInfo>> GetAllJobsAsync()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        var jobKeys = await scheduler.GetJobKeys(global::Quartz.Impl.Matchers.GroupMatcher<JobKey>.AnyGroup());
        var executingJobs = await scheduler.GetCurrentlyExecutingJobs();
        var jobs = new List<JobInfo>();
        var runSummary = await _jobLogQueryService.GetRunSummaryAsync();

        foreach (var jobKey in jobKeys)
        {
            if (jobKey.Name == "System_LogCleanup")
            {
                continue;
            }

            var detail = await scheduler.GetJobDetail(jobKey);
            if (detail == null)
            {
                continue;
            }

            var triggers = await scheduler.GetTriggersOfJob(jobKey);
            bool isRunning = executingJobs.Any(x => x.JobDetail.Key.Equals(jobKey));
            bool isDisabled = detail.JobDataMap.ContainsKey("IsDisabled")
                && bool.TryParse(detail.JobDataMap.GetString("IsDisabled"), out var disabled)
                && disabled;

            var jobInfo = CreateJobInfo(jobKey, detail, isRunning, isDisabled);
            string summaryKey = $"{jobKey.Group}::{jobKey.Name}";
            DateTime? jobLastRunTime = runSummary.LastRunTimes.TryGetValue(summaryKey, out var outDt) ? outDt.ToLocalTime() : null;

            var originalTriggers = ReadOriginalTriggers(detail);
            if (originalTriggers.Count > 0)
            {
                await AddOriginalTriggersAsync(scheduler, triggers, jobInfo, originalTriggers, jobLastRunTime);
            }
            else
            {
                await AddLiveTriggersAsync(scheduler, triggers, jobInfo, jobLastRunTime);
            }

            jobInfo.LastRunResult = runSummary.LastRunResults.TryGetValue(summaryKey, out var result) ? result : null;
            jobs.Add(jobInfo);
        }

        return jobs;
    }

    private static JobInfo CreateJobInfo(JobKey jobKey, IJobDetail detail, bool isRunning, bool isDisabled)
    {
        var jobInfo = new JobInfo
        {
            JobName = jobKey.Name,
            JobGroup = jobKey.Group,
            Description = detail.Description,
            FileName = detail.JobDataMap.ContainsKey("FileName") ? detail.JobDataMap.GetString("FileName") : null,
            Arguments = detail.JobDataMap.ContainsKey("Arguments") ? detail.JobDataMap.GetString("Arguments") : null,
            WorkingDirectory = detail.JobDataMap.ContainsKey("WorkingDirectory") ? detail.JobDataMap.GetString("WorkingDirectory") : null,
            Author = detail.JobDataMap.ContainsKey("Author") ? (detail.JobDataMap.GetString("Author") ?? string.Empty) : string.Empty,
            State = isRunning ? "執行中" : (isDisabled ? "已停用" : "就緒")
        };

        if (detail.JobDataMap.ContainsKey("MisfireActionFireAndProceed")
            && bool.TryParse(detail.JobDataMap.GetString("MisfireActionFireAndProceed"), out var misfire))
        {
            jobInfo.MisfireActionFireAndProceed = misfire;
        }

        if (detail.JobDataMap.ContainsKey("ConcurrencyRule"))
        {
            jobInfo.ConcurrencyRule = detail.JobDataMap.GetString("ConcurrencyRule") ?? "Parallel";
        }

        if (detail.JobDataMap.ContainsKey("IsHidden") && bool.TryParse(detail.JobDataMap.GetString("IsHidden"), out var hidden))
        {
            jobInfo.IsHidden = hidden;
        }

        if (detail.JobDataMap.ContainsKey("MaxRunTimeSeconds")
            && int.TryParse(detail.JobDataMap.GetString("MaxRunTimeSeconds"), out var maxRunTime))
        {
            jobInfo.MaxRunTimeSeconds = maxRunTime;
        }

        return jobInfo;
    }

    private static List<TriggerDto> ReadOriginalTriggers(IJobDetail detail)
    {
        if (!detail.JobDataMap.ContainsKey("OriginalTriggers"))
        {
            return new List<TriggerDto>();
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<TriggerDto>>(detail.JobDataMap.GetString("OriginalTriggers")!) ?? new();
        }
        catch
        {
            return new List<TriggerDto>();
        }
    }

    private static async Task AddOriginalTriggersAsync(IScheduler scheduler, IReadOnlyCollection<ITrigger> liveTriggers, JobInfo jobInfo, List<TriggerDto> originalTriggers, DateTime? jobLastRunTime)
    {
        foreach (var origT in originalTriggers)
        {
            origT.PreviousFireTime = jobLastRunTime;
            var liveT = liveTriggers.FirstOrDefault(x => x.Key.Name == origT.TriggerName && x.Key.Group == origT.TriggerGroup);
            if (liveT != null)
            {
                var state = await scheduler.GetTriggerState(liveT.Key);
                origT.State = ToDisplayState(state);
                origT.NextFireTime = liveT.GetNextFireTimeUtc()?.LocalDateTime;
            }
            else
            {
                origT.State = "已完成/過期";
                origT.NextFireTime = null;
            }

            jobInfo.Triggers.Add(origT);
        }
    }

    private static async Task AddLiveTriggersAsync(IScheduler scheduler, IReadOnlyCollection<ITrigger> triggers, JobInfo jobInfo, DateTime? jobLastRunTime)
    {
        foreach (var trigger in triggers)
        {
            var state = await scheduler.GetTriggerState(trigger.Key);
            jobInfo.Triggers.Add(new TriggerDto
            {
                TriggerName = trigger.Key.Name,
                TriggerGroup = trigger.Key.Group,
                Description = trigger.Description ?? string.Empty,
                CronExpression = GetCronExpression(trigger),
                StartAt = trigger.StartTimeUtc,
                EndAt = trigger.EndTimeUtc,
                NextFireTime = trigger.GetNextFireTimeUtc()?.LocalDateTime,
                PreviousFireTime = jobLastRunTime,
                RepeatIntervalMinutes = GetRepeatInterval(trigger),
                RepeatInterval = GetRepeatInterval(trigger),
                RepeatIntervalUnit = GetRepeatIntervalUnit(trigger),
                WeeklyInterval = GetWeeklyInterval(trigger),
                State = ToDisplayState(state)
            });
        }
    }

    private static string ToDisplayState(TriggerState state)
    {
        return state switch
        {
            TriggerState.Normal => "就緒",
            TriggerState.Paused => "已停用",
            TriggerState.Complete => "完成",
            TriggerState.Error => "發生錯誤",
            TriggerState.Blocked => "執行中",
            TriggerState.None => "就緒",
            _ => state.ToString()
        };
    }

    private static string? GetCronExpression(ITrigger trigger)
    {
        if (trigger is ICronTrigger cronTrigger)
        {
            return cronTrigger.CronExpressionString;
        }

        if (trigger is not IDailyTimeIntervalTrigger dailyTrigger)
        {
            return null;
        }

        int sec = dailyTrigger.StartTimeOfDay.Second;
        int min = dailyTrigger.StartTimeOfDay.Minute;
        int hour = dailyTrigger.StartTimeOfDay.Hour;

        if (dailyTrigger.DaysOfWeek.Count == 7)
        {
            return $"{sec} {min} {hour} * * ?";
        }

        if (dailyTrigger.DaysOfWeek.Count <= 0)
        {
            return null;
        }

        var daysMap = new Dictionary<DayOfWeek, string>
        {
            { DayOfWeek.Sunday, "SUN" },
            { DayOfWeek.Monday, "MON" },
            { DayOfWeek.Tuesday, "TUE" },
            { DayOfWeek.Wednesday, "WED" },
            { DayOfWeek.Thursday, "THU" },
            { DayOfWeek.Friday, "FRI" },
            { DayOfWeek.Saturday, "SAT" }
        };
        return $"{sec} {min} {hour} ? * {string.Join(",", dailyTrigger.DaysOfWeek.Select(d => daysMap[d]))}";
    }

    private static int? GetRepeatInterval(ITrigger trigger)
    {
        if (trigger.JobDataMap.ContainsKey("RepeatInterval") && int.TryParse(trigger.JobDataMap.GetString("RepeatInterval"), out var repeatInterval))
        {
            return repeatInterval;
        }

        if (trigger.JobDataMap.ContainsKey("RepeatIntervalMinutes") && int.TryParse(trigger.JobDataMap.GetString("RepeatIntervalMinutes"), out var repeatIntervalMinutes))
        {
            return repeatIntervalMinutes;
        }

        return null;
    }

    private static string? GetRepeatIntervalUnit(ITrigger trigger)
    {
        if (trigger.JobDataMap.ContainsKey("RepeatIntervalUnit"))
        {
            return trigger.JobDataMap.GetString("RepeatIntervalUnit") ?? "Minute";
        }

        return trigger.JobDataMap.ContainsKey("RepeatIntervalMinutes") ? "Minute" : null;
    }

    private static int? GetWeeklyInterval(ITrigger trigger)
    {
        return trigger.JobDataMap.ContainsKey("WeeklyInterval")
            && int.TryParse(trigger.JobDataMap.GetString("WeeklyInterval"), out var weeklyInterval)
                ? weeklyInterval
                : null;
    }
}
