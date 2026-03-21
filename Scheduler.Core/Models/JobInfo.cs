using System;
using System.Collections.Generic;

namespace Scheduler.Core.Models;

public class JobInfo
{
    public string JobName { get; set; } = string.Empty;
    public string JobGroup { get; set; } = string.Empty;
    public string? Description { get; set; }
    
    // Job composite state (Running, Ready, Error) based on Triggers
    public string State { get; set; } = string.Empty;

    // Job Data Map details
    public string? FileName { get; set; }
    public string? Arguments { get; set; }
    public string? WorkingDirectory { get; set; }
    public int? MaxRunTimeSeconds { get; set; }

    public bool MisfireActionFireAndProceed { get; set; } = true;
    public string ConcurrencyRule { get; set; } = "Parallel";
    public bool IsHidden { get; set; } = false;

    // 關聯的多重觸發程序
    public List<TriggerDto> Triggers { get; set; } = new();
}
