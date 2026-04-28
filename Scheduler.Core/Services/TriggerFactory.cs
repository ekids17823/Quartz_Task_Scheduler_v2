using Quartz;
using Scheduler.Core.Models;

namespace Scheduler.Core.Services;

public interface ITriggerFactory
{
    ITrigger? BuildForCreate(JobKey jobKey, TriggerDto triggerDto, bool misfireActionFireAndProceed, DateTimeOffset? now = null);
    ITrigger? BuildForResume(JobKey jobKey, TriggerDto triggerDto, bool misfireActionFireAndProceed, DateTimeOffset? now = null);
}

public class TriggerFactory : ITriggerFactory
{
    private static readonly TimeSpan SafetyDelay = TimeSpan.FromSeconds(2);

    public ITrigger? BuildForCreate(JobKey jobKey, TriggerDto triggerDto, bool misfireActionFireAndProceed, DateTimeOffset? now = null)
    {
        var effectiveNow = now ?? DateTimeOffset.UtcNow;
        var tb = CreateBaseBuilder(jobKey, triggerDto, effectiveNow);
        int repVal = triggerDto.RepeatInterval ?? triggerDto.RepeatIntervalMinutes ?? 0;
        string repUnit = string.IsNullOrWhiteSpace(triggerDto.RepeatIntervalUnit) ? "Minute" : triggerDto.RepeatIntervalUnit;

        if (!ApplySchedule(tb, triggerDto, repVal, repUnit, misfireActionFireAndProceed, skipPastOneTimeWithoutMisfire: true, effectiveNow))
        {
            return null;
        }

        var builtTrigger = tb.Build();

        if (!misfireActionFireAndProceed)
        {
            return MovePastStartToNextFutureFire(tb, builtTrigger, effectiveNow);
        }

        return LimitSimpleMisfireCatchUp(tb, builtTrigger, effectiveNow);
    }

    public ITrigger? BuildForResume(JobKey jobKey, TriggerDto triggerDto, bool misfireActionFireAndProceed, DateTimeOffset? now = null)
    {
        var effectiveNow = now ?? DateTimeOffset.UtcNow;
        var tb = CreateBaseBuilder(jobKey, triggerDto, effectiveNow);
        int repVal = triggerDto.RepeatInterval ?? triggerDto.RepeatIntervalMinutes ?? 0;
        string repUnit = string.IsNullOrWhiteSpace(triggerDto.RepeatIntervalUnit) ? "Minute" : triggerDto.RepeatIntervalUnit;

        if (!ApplySchedule(tb, triggerDto, repVal, repUnit, misfireActionFireAndProceed, skipPastOneTimeWithoutMisfire: false, effectiveNow))
        {
            return null;
        }

        var builtTrigger = tb.Build();
        var nextFire = builtTrigger.GetFireTimeAfter(effectiveNow.Add(SafetyDelay));
        if (!nextFire.HasValue)
        {
            return null;
        }

        tb.StartAt(nextFire.Value);
        return tb.Build();
    }

    private static TriggerBuilder CreateBaseBuilder(JobKey jobKey, TriggerDto triggerDto, DateTimeOffset now)
    {
        var triggerKey = new TriggerKey(triggerDto.TriggerName, triggerDto.TriggerGroup);
        var tb = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .WithDescription(triggerDto.Description);

        if (triggerDto.StartAt.HasValue)
        {
            tb.StartAt(triggerDto.StartAt.Value);
        }

        if (triggerDto.EndAt.HasValue)
        {
            tb.EndAt(triggerDto.EndAt.Value);
        }
        else if (string.IsNullOrWhiteSpace(triggerDto.CronExpression)
            && triggerDto.RepeatDuration.HasValue
            && triggerDto.RepeatDuration.Value > 0)
        {
            DateTimeOffset baseStart = triggerDto.StartAt ?? now;
            tb.EndAt(baseStart.Add(ToTimeSpan(triggerDto.RepeatDuration.Value, triggerDto.RepeatDurationUnit)));
        }

        int repVal = triggerDto.RepeatInterval ?? triggerDto.RepeatIntervalMinutes ?? 0;
        string repUnit = string.IsNullOrWhiteSpace(triggerDto.RepeatIntervalUnit) ? "Minute" : triggerDto.RepeatIntervalUnit;
        if (repVal > 0)
        {
            tb.UsingJobData("RepeatInterval", repVal.ToString());
            tb.UsingJobData("RepeatIntervalUnit", repUnit);
        }

        if (triggerDto.WeeklyInterval.HasValue && triggerDto.WeeklyInterval.Value > 1)
        {
            tb.UsingJobData("WeeklyInterval", triggerDto.WeeklyInterval.Value.ToString());
        }

        return tb;
    }

    private static bool ApplySchedule(
        TriggerBuilder tb,
        TriggerDto triggerDto,
        int repVal,
        string repUnit,
        bool misfireActionFireAndProceed,
        bool skipPastOneTimeWithoutMisfire,
        DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(triggerDto.CronExpression))
        {
            ApplyCronOrDailySchedule(tb, triggerDto, repVal, repUnit, misfireActionFireAndProceed);
            return true;
        }

        if (repVal > 0)
        {
            tb.WithSimpleSchedule(x =>
            {
                if (repUnit == "Second") x.WithIntervalInSeconds(repVal).RepeatForever();
                else if (repUnit == "Hour") x.WithIntervalInHours(repVal).RepeatForever();
                else x.WithIntervalInMinutes(repVal).RepeatForever();

                if (misfireActionFireAndProceed) x.WithMisfireHandlingInstructionIgnoreMisfires();
                else x.WithMisfireHandlingInstructionNextWithExistingCount();
            });
            return true;
        }

        if (skipPastOneTimeWithoutMisfire
            && !misfireActionFireAndProceed
            && triggerDto.StartAt.HasValue
            && triggerDto.StartAt.Value < now)
        {
            return false;
        }

        tb.WithSimpleSchedule(x =>
        {
            x.WithRepeatCount(0);
            if (misfireActionFireAndProceed) x.WithMisfireHandlingInstructionFireNow();
        });
        return true;
    }

    private static ITrigger MovePastStartToNextFutureFire(TriggerBuilder tb, ITrigger builtTrigger, DateTimeOffset now)
    {
        if (builtTrigger.StartTimeUtc <= now)
        {
            var nextFire = builtTrigger.GetFireTimeAfter(now.Add(SafetyDelay));
            if (nextFire.HasValue)
            {
                tb.StartAt(nextFire.Value);
                return tb.Build();
            }
        }

        return builtTrigger;
    }

    private static ITrigger LimitSimpleMisfireCatchUp(TriggerBuilder tb, ITrigger builtTrigger, DateTimeOffset now)
    {
        if (builtTrigger is not ISimpleTrigger simpleTrigger || simpleTrigger.StartTimeUtc >= now)
        {
            return builtTrigger;
        }

        var interval = simpleTrigger.RepeatInterval;
        if (interval.TotalMilliseconds <= 0)
        {
            return builtTrigger;
        }

        long diffMs = (long)(now - simpleTrigger.StartTimeUtc).TotalMilliseconds;
        long skipCounts = diffMs / (long)interval.TotalMilliseconds;
        if (skipCounts <= 0)
        {
            return builtTrigger;
        }

        tb.StartAt(simpleTrigger.StartTimeUtc.AddMilliseconds(skipCounts * interval.TotalMilliseconds));
        return tb.Build();
    }

    private static void ApplyCronOrDailySchedule(TriggerBuilder tb, TriggerDto triggerDto, int repVal, string repUnit, bool misfireActionFireAndProceed)
    {
        if (repVal > 0)
        {
            var parts = triggerDto.CronExpression!.Split(' ');
            if (parts.Length >= 6
                && int.TryParse(parts[0], out int sec)
                && int.TryParse(parts[1], out int min)
                && int.TryParse(parts[2], out int hour))
            {
                string dow = parts[5];
                tb.WithDailyTimeIntervalSchedule(x =>
                {
                    if (repUnit == "Second") x.WithIntervalInSeconds(repVal);
                    else if (repUnit == "Hour") x.WithIntervalInHours(repVal);
                    else x.WithIntervalInMinutes(repVal);

                    x.StartingDailyAt(new TimeOfDay(hour, min, sec));

                    if (triggerDto.RepeatDuration.HasValue && triggerDto.RepeatDuration.Value > 0)
                    {
                        var startDt = DateTime.Today.AddHours(hour).AddMinutes(min).AddSeconds(sec);
                        var endDt = startDt.Add(ToTimeSpan(triggerDto.RepeatDuration.Value, triggerDto.RepeatDurationUnit));
                        x.EndingDailyAt(endDt.Date > startDt.Date
                            ? new TimeOfDay(23, 59, 59)
                            : new TimeOfDay(endDt.Hour, endDt.Minute, endDt.Second));
                    }

                    ApplyDaysOfWeek(x, dow);
                    if (misfireActionFireAndProceed) x.WithMisfireHandlingInstructionFireAndProceed();
                    else x.WithMisfireHandlingInstructionDoNothing();
                });
                return;
            }
        }

        tb.WithCronSchedule(triggerDto.CronExpression!, x =>
        {
            if (misfireActionFireAndProceed) x.WithMisfireHandlingInstructionFireAndProceed();
            else x.WithMisfireHandlingInstructionDoNothing();
        });
    }

    private static void ApplyDaysOfWeek(DailyTimeIntervalScheduleBuilder builder, string dow)
    {
        if (dow == "?" || dow == "*")
        {
            builder.OnEveryDay();
            return;
        }

        var days = new List<DayOfWeek>();
        if (dow.Contains("SUN")) days.Add(DayOfWeek.Sunday);
        if (dow.Contains("MON")) days.Add(DayOfWeek.Monday);
        if (dow.Contains("TUE")) days.Add(DayOfWeek.Tuesday);
        if (dow.Contains("WED")) days.Add(DayOfWeek.Wednesday);
        if (dow.Contains("THU")) days.Add(DayOfWeek.Thursday);
        if (dow.Contains("FRI")) days.Add(DayOfWeek.Friday);
        if (dow.Contains("SAT")) days.Add(DayOfWeek.Saturday);

        if (days.Count > 0) builder.OnDaysOfTheWeek(days.ToArray());
        else builder.OnEveryDay();
    }

    private static TimeSpan ToTimeSpan(int value, string? unit)
    {
        return unit == "Hour" ? TimeSpan.FromHours(value)
            : unit == "Day" ? TimeSpan.FromDays(value)
            : TimeSpan.FromMinutes(value);
    }
}
