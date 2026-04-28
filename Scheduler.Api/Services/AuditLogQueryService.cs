using Microsoft.Data.Sqlite;
using Scheduler.Core.Models;

namespace Scheduler.Api.Services;

public interface IAuditLogQueryService
{
    Task<List<AuditLogEntry>> GetAuditLogsAsync();
}

public class AuditLogQueryService : IAuditLogQueryService
{
    public async Task<List<AuditLogEntry>> GetAuditLogsAsync()
    {
        string dbPath = Path.Combine(Directory.GetCurrentDirectory(), "quartz.db");
        await using var conn = new SqliteConnection($"Data Source={dbPath};");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, EventId, EventTimeUtc, JobName, JobGroup, Description, AccountName FROM AuditLogs ORDER BY EventTimeUtc DESC, Id DESC LIMIT 1000";

        var logs = new List<AuditLogEntry>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
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

        return logs;
    }
}
