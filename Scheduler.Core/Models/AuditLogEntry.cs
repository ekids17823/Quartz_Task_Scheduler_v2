using System;

namespace Scheduler.Core.Models;

public class AuditLogEntry
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public DateTime EventTimeUtc { get; set; }
    public string JobName { get; set; } = string.Empty;
    public string JobGroup { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
}
