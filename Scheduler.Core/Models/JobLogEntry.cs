using System;

namespace Scheduler.Core.Models;

public class JobLogEntry
{
    public int Id { get; set; }
    public string JobName { get; set; } = string.Empty;
    public string JobGroup { get; set; } = string.Empty;
    public DateTime FireTimeUtc { get; set; }
    public long RunTimeMs { get; set; }
    public bool IsSuccess { get; set; }
    public int? ExitCode { get; set; }
    public string? StdOut { get; set; }
    public string? StdErr { get; set; }
    public string? ErrorMessage { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public int EventId { get; set; }
}
