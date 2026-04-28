using Microsoft.Data.Sqlite;

namespace Scheduler.Core.Services;

public interface IAuditLogService
{
    Task SaveAsync(int eventId, string jobName, string jobGroup, string description);
}

public class AuditLogService : IAuditLogService
{
    public async Task SaveAsync(int eventId, string jobName, string jobGroup, string description)
    {
        try
        {
            string dbPath = Path.Combine(Directory.GetCurrentDirectory(), "quartz.db");
            await using var conn = new SqliteConnection($"Data Source={dbPath};");
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO AuditLogs (EventId, EventTimeUtc, JobName, JobGroup, Description, AccountName)
                VALUES (@EventId, @EventTimeUtc, @JobName, @JobGroup, @Description, @AccountName)";
            cmd.Parameters.AddWithValue("@EventId", eventId);
            cmd.Parameters.AddWithValue("@EventTimeUtc", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@JobName", jobName);
            cmd.Parameters.AddWithValue("@JobGroup", jobGroup);
            cmd.Parameters.AddWithValue("@Description", description);
            cmd.Parameters.AddWithValue("@AccountName", Environment.UserDomainName + "\\" + Environment.UserName);
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Audit logging must not block scheduler operations.
        }
    }
}
