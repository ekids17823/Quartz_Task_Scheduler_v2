using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using Quartz;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Scheduler.Core.Jobs;

// 系統內部排程：負責定時清除陳舊的紀錄與壓縮資料庫
public class LogCleanupJob : IJob
{
    private readonly ILogger<LogCleanupJob> _logger;
    private readonly IConfiguration _configuration;

    public LogCleanupJob(ILogger<LogCleanupJob> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        // 分別讀取排程日誌與稽核日誌的保留天數
        int jobLogDays = 30;
        if (int.TryParse(_configuration["JobExecutionLogRetentionDays"], out int parsedJob)) jobLogDays = parsedJob;
        
        int auditLogDays = 365;
        if (int.TryParse(_configuration["AuditLogRetentionDays"], out int parsedAudit)) auditLogDays = parsedAudit;
        
        DateTime jobCutoffDate = DateTime.UtcNow.AddDays(-jobLogDays);
        DateTime auditCutoffDate = DateTime.UtcNow.AddDays(-auditLogDays);
        
        _logger.LogInformation("[系統自動清理] 開始維護：排程紀錄清除 {JobCutoff} 前 (保 {JobDays} 天)；稽核紀錄清除 {AuditCutoff} 前 (保 {AuditDays} 天)。", 
            jobCutoffDate.ToString("yyyy-MM-dd"), jobLogDays, auditCutoffDate.ToString("yyyy-MM-dd"), auditLogDays);

        string dbPath = Path.Combine(Directory.GetCurrentDirectory(), "quartz.db");
        using var conn = new SqliteConnection($"Data Source={dbPath};");
        await conn.OpenAsync();
        
        // 1. 刪除過期的 JobExecutionLogs
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM JobExecutionLogs WHERE FireTimeUtc < @Cutoff";
        cmd.Parameters.AddWithValue("@Cutoff", jobCutoffDate);
        int deletedExecutionLogs = await cmd.ExecuteNonQueryAsync();

        // 2. 刪除過期的 AuditLogs
        using var auditCmd = conn.CreateCommand();
        auditCmd.CommandText = "DELETE FROM AuditLogs WHERE EventTimeUtc < @Cutoff";
        auditCmd.Parameters.AddWithValue("@Cutoff", auditCutoffDate);
        int deletedAuditLogs = await auditCmd.ExecuteNonQueryAsync();

        _logger.LogInformation("[系統自動清理] 完成。共移除了 {ExecCount} 筆排程任務日誌與 {AuditCount} 筆稽核日誌。", deletedExecutionLogs, deletedAuditLogs);

        // 3. 壓縮資料庫以釋放閒置的實體硬碟空間
        if (deletedExecutionLogs > 0 || deletedAuditLogs > 0)
        {
            try
            {
                _logger.LogInformation("[系統自動清理] 正對資料庫執行 VACUUM 壓縮與空間回收...");
                using var vacuumCmd = conn.CreateCommand();
                vacuumCmd.CommandText = "VACUUM;";
                await vacuumCmd.ExecuteNonQueryAsync();
                _logger.LogInformation("[系統自動清理] 空間回收與最佳化作業完畢。");
            }
            // 忽略因高頻寫入而導致 VACUUM 無法獲取專屬鎖的情況
            catch (Exception ex) 
            {
                _logger.LogWarning("[系統自動清理] VACUUM 失敗，可能是資料庫目前正在高頻寫入中: {Msg}", ex.Message);
            }
        }
    }
}
