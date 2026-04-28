using Microsoft.Data.Sqlite;
using Scheduler.Core.Constants;
using Scheduler.Core.Models;
using Scheduler.Core.Services;

namespace Scheduler.Api.Services;

public interface IJobLogQueryService
{
    Task<JobRunSummary> GetRunSummaryAsync();
    Task<List<JobLogEntry>> GetJobLogsAsync(string group, string name);
}

public sealed class JobRunSummary
{
    public Dictionary<string, string> LastRunResults { get; } = new();
    public Dictionary<string, DateTime> LastRunTimes { get; } = new();
}

public class JobLogQueryService : IJobLogQueryService
{
    public async Task<JobRunSummary> GetRunSummaryAsync()
    {
        var summary = new JobRunSummary();

        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT JobGroup, JobName, ExitCode, IsSuccess, ErrorMessage, FireTimeUtc, EventId FROM JobExecutionLogs ORDER BY FireTimeUtc DESC, Id DESC";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string key = $"{reader.GetString(0)}::{reader.GetString(1)}";
                int eventId = reader.GetInt32(6);
                bool isSuccess = reader.GetInt32(3) == 1;
                string? errMsg = reader.IsDBNull(4) ? null : reader.GetString(4);
                int? exitCode = reader.IsDBNull(2) ? null : reader.GetInt32(2);

                if (!summary.LastRunResults.ContainsKey(key))
                {
                    var result = JobLogDisplayMapper.ToLastRunResult(eventId, isSuccess, exitCode, errMsg);
                    if (result != null)
                    {
                        summary.LastRunResults[key] = result;
                    }
                }

                if (!summary.LastRunTimes.ContainsKey(key) && SchedulerEventIds.IsTriggerEvent(eventId))
                {
                    summary.LastRunTimes[key] = reader.GetDateTime(5);
                }
            }
        }
        catch
        {
            // The scheduler list should still load even when optional log summary lookup fails.
        }

        return summary;
    }

    public async Task<List<JobLogEntry>> GetJobLogsAsync(string group, string name)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM JobExecutionLogs WHERE JobName = @JobName AND JobGroup = @JobGroup AND FireTimeUtc >= datetime('now', '-7 days') ORDER BY FireTimeUtc DESC, Id DESC LIMIT 2000";
        cmd.Parameters.AddWithValue("@JobName", name);
        cmd.Parameters.AddWithValue("@JobGroup", group);

        var logs = new List<JobLogEntry>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            bool isSuccess = reader.GetInt32(5) == 1;
            string? errMsg = reader.IsDBNull(9) ? null : reader.GetString(9);
            int eventId = reader.FieldCount > 11 && !reader.IsDBNull(11) ? reader.GetInt32(11) : 0;
            eventId = JobLogDisplayMapper.NormalizeLegacyEventId(eventId, isSuccess, errMsg);

            logs.Add(new JobLogEntry
            {
                Id = reader.GetInt32(0),
                JobName = reader.GetString(1),
                JobGroup = reader.GetString(2),
                FireTimeUtc = reader.GetDateTime(3),
                RunTimeMs = reader.GetInt64(4),
                IsSuccess = isSuccess,
                ExitCode = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                StdOut = reader.IsDBNull(7) ? null : reader.GetString(7),
                StdErr = reader.IsDBNull(8) ? null : reader.GetString(8),
                ErrorMessage = errMsg,
                CorrelationId = reader.FieldCount > 10 && !reader.IsDBNull(10) ? reader.GetString(10) : string.Empty,
                EventId = eventId
            });
        }

        return logs;
    }

    private static SqliteConnection CreateConnection()
    {
        string dbPath = Path.Combine(Directory.GetCurrentDirectory(), "quartz.db");
        return new SqliteConnection($"Data Source={dbPath};");
    }
}
