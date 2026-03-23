using System;

namespace Scheduler.Core.Models;

public class TriggerDto
{
    public string TriggerName { get; set; } = Guid.NewGuid().ToString();
    public string TriggerGroup { get; set; } = "DefaultGroup";
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// 若為空，則代表是簡易迴圈排程 (SimpleTrigger)
    /// </summary>
    public string? CronExpression { get; set; }
    
    // 進階排程設定
    public DateTimeOffset? StartAt { get; set; }
    public DateTimeOffset? EndAt { get; set; }
    
    // 單純迴圈設定
    public int? RepeatInterval { get; set; }
    public string? RepeatIntervalUnit { get; set; } // Second, Minute, Hour
    public int? RepeatIntervalMinutes { get; set; } // Legacy fallback
    public int? RepeatDuration { get; set; }
    public string? RepeatDurationUnit { get; set; }
    public int? WeeklyInterval { get; set; }
    
    // 狀態顯示專用
    public string State { get; set; } = string.Empty;
    public DateTime? NextFireTime { get; set; }
    public DateTime? PreviousFireTime { get; set; }
    
    public string HumanDescription => Scheduler.Core.Helpers.TriggerDescriptionHelper.GetDescription(this);
    public string TriggerType => Scheduler.Core.Helpers.TriggerDescriptionHelper.GetTriggerType(this);
}
