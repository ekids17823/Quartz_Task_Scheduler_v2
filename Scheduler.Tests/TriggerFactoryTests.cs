using Quartz;
using Scheduler.Core.Models;
using Scheduler.Core.Services;
using Xunit;

namespace Scheduler.Tests;

public class TriggerFactoryTests
{
    private readonly TriggerFactory _factory = new();
    private readonly JobKey _jobKey = new("TestJob", "TestGroup");

    [Fact]
    public void BuildForCreate_OneTimePastWithoutMisfire_ReturnsNull()
    {
        var now = new DateTimeOffset(2026, 4, 29, 10, 0, 0, TimeSpan.Zero);
        var triggerDto = new TriggerDto
        {
            TriggerName = "PastOnce",
            TriggerGroup = "TestGroup",
            StartAt = now.AddMinutes(-5)
        };

        var trigger = _factory.BuildForCreate(_jobKey, triggerDto, misfireActionFireAndProceed: false, now);

        Assert.Null(trigger);
    }

    [Fact]
    public void BuildForCreate_IntervalWithoutMisfire_StartsAtNextFutureFire()
    {
        var now = new DateTimeOffset(2026, 4, 29, 10, 0, 0, TimeSpan.Zero);
        var triggerDto = new TriggerDto
        {
            TriggerName = "Interval",
            TriggerGroup = "TestGroup",
            StartAt = now.AddMinutes(-10),
            RepeatInterval = 5,
            RepeatIntervalUnit = "Minute"
        };

        var trigger = _factory.BuildForCreate(_jobKey, triggerDto, misfireActionFireAndProceed: false, now);

        Assert.NotNull(trigger);
        Assert.True(trigger.StartTimeUtc > now);
        Assert.True(trigger.StartTimeUtc >= now.AddMinutes(5));
    }

    [Fact]
    public void BuildForCreate_IntervalWithMisfire_LimitsCatchUpToRecentCycle()
    {
        var now = new DateTimeOffset(2026, 4, 29, 10, 0, 0, TimeSpan.Zero);
        var triggerDto = new TriggerDto
        {
            TriggerName = "IntervalCatchUp",
            TriggerGroup = "TestGroup",
            StartAt = now.AddHours(-2),
            RepeatInterval = 5,
            RepeatIntervalUnit = "Minute"
        };

        var trigger = _factory.BuildForCreate(_jobKey, triggerDto, misfireActionFireAndProceed: true, now);

        Assert.NotNull(trigger);
        Assert.True(trigger.StartTimeUtc <= now);
        Assert.True(trigger.StartTimeUtc > now.AddMinutes(-6));
    }

    [Fact]
    public void BuildForResume_IgnoresMissedDisabledTime_AndStartsInFuture()
    {
        var now = new DateTimeOffset(2026, 4, 29, 10, 0, 0, TimeSpan.Zero);
        var triggerDto = new TriggerDto
        {
            TriggerName = "ResumeInterval",
            TriggerGroup = "TestGroup",
            StartAt = now.AddHours(-1),
            RepeatInterval = 1,
            RepeatIntervalUnit = "Minute"
        };

        var trigger = _factory.BuildForResume(_jobKey, triggerDto, misfireActionFireAndProceed: true, now);

        Assert.NotNull(trigger);
        Assert.True(trigger.StartTimeUtc > now);
    }

    [Fact]
    public void BuildForCreate_WeeklyTrigger_PreservesWeeklyIntervalJobData()
    {
        var now = new DateTimeOffset(2026, 4, 29, 10, 0, 0, TimeSpan.Zero);
        var triggerDto = new TriggerDto
        {
            TriggerName = "Weekly",
            TriggerGroup = "TestGroup",
            StartAt = now,
            CronExpression = "0 0 10 ? * MON,WED",
            WeeklyInterval = 2
        };

        var trigger = _factory.BuildForCreate(_jobKey, triggerDto, misfireActionFireAndProceed: false, now);

        Assert.NotNull(trigger);
        Assert.Equal("2", trigger.JobDataMap.GetString("WeeklyInterval"));
    }

    [Fact]
    public void BuildForCreate_CronWithRepeat_UsesDailyTimeIntervalTrigger()
    {
        var now = new DateTimeOffset(2026, 4, 29, 8, 0, 0, TimeSpan.Zero);
        var triggerDto = new TriggerDto
        {
            TriggerName = "DailyRepeat",
            TriggerGroup = "TestGroup",
            StartAt = now,
            CronExpression = "0 0 9 * * ?",
            RepeatInterval = 5,
            RepeatIntervalUnit = "Minute",
            RepeatDuration = 1,
            RepeatDurationUnit = "Hour"
        };

        var trigger = _factory.BuildForCreate(_jobKey, triggerDto, misfireActionFireAndProceed: false, now);

        Assert.IsAssignableFrom<IDailyTimeIntervalTrigger>(trigger);
    }
}
