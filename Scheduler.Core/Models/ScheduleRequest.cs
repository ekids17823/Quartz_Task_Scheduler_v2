using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Scheduler.Core.Models;

public class ScheduleRequest
{
    [Required]
    public string JobName { get; set; } = string.Empty;

    public string JobGroup { get; set; } = "DefaultGroup";

    public string Description { get; set; } = string.Empty;

    [Required]
    public string FileName { get; set; } = string.Empty;

    public string Arguments { get; set; } = string.Empty;
    
    public string WorkingDirectory { get; set; } = string.Empty;

    // 工作層級的進階設定：若執行時間超過 N 秒就停止
    public int? MaxRunTimeSeconds { get; set; }

    public bool MisfireActionFireAndProceed { get; set; } = true;
    public string ConcurrencyRule { get; set; } = "Parallel";
    public bool IsHidden { get; set; } = false;
    public string Author { get; set; } = string.Empty;

    // 多重觸發程序
    public List<TriggerDto> Triggers { get; set; } = new();
}
