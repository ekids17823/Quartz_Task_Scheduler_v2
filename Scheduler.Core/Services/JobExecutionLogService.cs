using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Scheduler.Core.Services;

public interface IJobExecutionLogService
{
    Task SaveAsync(int eventId, string correlation, string name, string group, DateTime fireTime, long runTime, bool isSuccess, int? exitCode, string? stdOut, string? stdErr, string? error);
}

public class JobExecutionLogService : IJobExecutionLogService
{
    private readonly ILogger<JobExecutionLogService> _logger;

    public JobExecutionLogService(ILogger<JobExecutionLogService> logger)
    {
        _logger = logger;
    }

    public async Task SaveAsync(int eventId, string correlation, string name, string group, DateTime fireTime, long runTime, bool isSuccess, int? exitCode, string? stdOut, string? stdErr, string? error)
    {
        try
        {
            string dbPath = Path.Combine(Directory.GetCurrentDirectory(), "quartz.db");
            await using var conn = new SqliteConnection($"Data Source={dbPath};");
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO JobExecutionLogs
                (JobName, JobGroup, FireTimeUtc, RunTimeMs, IsSuccess, ExitCode, StdOut, StdErr, ErrorMessage, CorrelationId, EventId)
                VALUES (@Name, @Group, @FireTime, @RunTime, @IsSuccess, @ExitCode, @StdOut, @StdErr, @Error, @CorrId, @EventId)";

            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Group", group);
            cmd.Parameters.AddWithValue("@FireTime", fireTime);
            cmd.Parameters.AddWithValue("@RunTime", runTime);
            cmd.Parameters.AddWithValue("@IsSuccess", isSuccess ? 1 : 0);
            cmd.Parameters.AddWithValue("@ExitCode", (object?)exitCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@StdOut", (object?)stdOut ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@StdErr", (object?)stdErr ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Error", (object?)error ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CorrId", correlation);
            cmd.Parameters.AddWithValue("@EventId", eventId);

            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "無法寫入排程執行日誌。");
        }
    }
}
