using Quartz;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using System.Text;
using System;
using System.Threading.Tasks;
using System.Threading;

namespace Scheduler.Core.Jobs;

public class ProcessRunnerJob : IJob
{
    private readonly ILogger<ProcessRunnerJob> _logger;

    public ProcessRunnerJob(ILogger<ProcessRunnerJob> logger)
    {
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var dataMap = context.MergedJobDataMap;
        var fileName = dataMap.ContainsKey("FileName") ? dataMap.GetString("FileName") : null;
        var arguments = dataMap.ContainsKey("Arguments") ? dataMap.GetString("Arguments") : string.Empty;
        var workingDirectory = dataMap.ContainsKey("WorkingDirectory") ? dataMap.GetString("WorkingDirectory") : string.Empty;
        
        var maxRunTimeSecondsStr = dataMap.ContainsKey("MaxRunTimeSeconds") ? dataMap.GetString("MaxRunTimeSeconds") : null;
        int? maxRunTimeSeconds = int.TryParse(maxRunTimeSecondsStr, out var v) ? v : null;

        var jobKey = context.JobDetail.Key;
        
        bool isManual = context.MergedJobDataMap.ContainsKey("TriggerReason") && context.MergedJobDataMap.GetString("TriggerReason") == "Manual";
        int triggerEventId = isManual ? 110 : 107;
        
        SaveLog(triggerEventId, correlationId, jobKey.Name, jobKey.Group, DateTime.UtcNow, 0, true, null, null, null, null);
        
        // WeeklyInterval 跳過機制
        if (context.Trigger.JobDataMap.ContainsKey("WeeklyInterval"))
        {
            if (int.TryParse(context.Trigger.JobDataMap.GetString("WeeklyInterval"), out int wInt) && wInt > 1)
            {
                var startDt = context.Trigger.StartTimeUtc.LocalDateTime.Date;
                var nowDt = DateTimeOffset.UtcNow.LocalDateTime.Date;
                var startWeekStart = startDt.AddDays(-(int)startDt.DayOfWeek);
                var currentWeekStart = nowDt.AddDays(-(int)nowDt.DayOfWeek);
                int weeksPassed = (int)Math.Round((currentWeekStart - startWeekStart).TotalDays / 7.0);
                
                if (weeksPassed % wInt != 0)
                {
                    _logger.LogInformation("Job {JobKey} skipped this execution because it is an 'off' week (WeeklyInterval = {wInt}).", jobKey, wInt);
                    return; // 放棄執行
                }
            }
        }

        var concurrencyRule = dataMap.ContainsKey("ConcurrencyRule") ? dataMap.GetString("ConcurrencyRule") : "Parallel";
        
        var executingJobs = await context.Scheduler.GetCurrentlyExecutingJobs();
        var otherInstances = System.Linq.Enumerable.Where(executingJobs, x => x.JobDetail.Key.Equals(jobKey) && x.FireInstanceId != context.FireInstanceId).ToList();

        if (otherInstances.Any())
        {
            if (concurrencyRule == "DoNotStart")
            {
                _logger.LogInformation("Job {JobKey} aborted because another instance is running (Rule: DoNotStart).", jobKey);
                SaveLog(322, correlationId, jobKey.Name, jobKey.Group, DateTime.UtcNow, 0, false, null, null, null, "因並發規則 (不要啟動新執行個體) 而跳過該次執行。");
                return;
            }
            else if (concurrencyRule == "StopExisting")
            {
                _logger.LogInformation("Job {JobKey} stopping existing instances (Rule: StopExisting).", jobKey);
                foreach(var inst in otherInstances)
                {
                    await context.Scheduler.Interrupt(inst.FireInstanceId);
                }
            }
        }

        var stopwatch = Stopwatch.StartNew();
        bool isSuccess = false;
        int? exitCode = null;
        int? finalEventIdOverride = null;
        var stdOutBuilder = new StringBuilder();
        var stdErrBuilder = new StringBuilder();
        string? errorMessage = null;

        if (string.IsNullOrWhiteSpace(fileName))
        {
            errorMessage = "執行緒失敗: 未提供 FileName";
            _logger.LogError(errorMessage);
            SaveLog(203, correlationId, jobKey.Name, jobKey.Group, DateTime.UtcNow, stopwatch.ElapsedMilliseconds, false, null, null, null, errorMessage);
            return;
        }

        // [129] 已建立工作處理程序
        SaveLog(129, correlationId, jobKey.Name, jobKey.Group, DateTime.UtcNow, stopwatch.ElapsedMilliseconds, true, null, null, null, null);

        _logger.LogInformation("執行緒 [{JobKey}] 開始啟動程序: {FileName} {Arguments}", jobKey, fileName, arguments);

        try
        {
            bool isHidden = dataMap.ContainsKey("IsHidden") ? dataMap.GetBooleanValueFromString("IsHidden") : false;

            if (!string.IsNullOrWhiteSpace(workingDirectory) && !fileName.Contains('\\') && !fileName.Contains('/'))
            {
                string combinedPath = System.IO.Path.Combine(workingDirectory, fileName);
                if (System.IO.File.Exists(combinedPath))
                {
                    fileName = combinedPath; // 修正為絕對路徑
                }
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory
            };

            if (isHidden)
            {
                processStartInfo.UseShellExecute = false;
                processStartInfo.CreateNoWindow = true;
                processStartInfo.RedirectStandardOutput = true;
                processStartInfo.RedirectStandardError = true;
                
                // 解決中文 Windows 環境下呼叫 ping, ipconfig 等傳統 cmd 命令產生亂碼的問題
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                try 
                {
                    // 嘗試取得系統當前的 OEM CodePage (繁中通常為 950/Big5)
                    int codePage = System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
                    var encoding = Encoding.GetEncoding(codePage > 0 ? codePage : 950);
                    processStartInfo.StandardOutputEncoding = encoding;
                    processStartInfo.StandardErrorEncoding = encoding;
                } 
                catch 
                {
                    // 萬一找不到對應的編碼就退回 UTF-8
                    processStartInfo.StandardOutputEncoding = Encoding.UTF8;
                    processStartInfo.StandardErrorEncoding = Encoding.UTF8;
                }
            }
            else
            {
                // 用原生的 Shell 彈出終端機畫面讓使用者肉眼可見並進行互動
                processStartInfo.UseShellExecute = true;
                processStartInfo.CreateNoWindow = false;
                processStartInfo.RedirectStandardOutput = false;
                processStartInfo.RedirectStandardError = false;
            }

            using var process = new Process { StartInfo = processStartInfo };

            if (isHidden)
            {
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        stdOutBuilder.AppendLine(e.Data);
                        _logger.LogInformation("[{JobKey}] STDOUT: {Data}", jobKey, e.Data);
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        stdErrBuilder.AppendLine(e.Data);
                        _logger.LogWarning("[{JobKey}] STDERR: {Data}", jobKey, e.Data);
                    }
                };
            }

            process.Start();
            
            // [100] 工作已開始
            SaveLog(100, correlationId, jobKey.Name, jobKey.Group, DateTime.UtcNow, stopwatch.ElapsedMilliseconds, true, null, null, null, null);
            // [200] 動作已經啟動
            SaveLog(200, correlationId, jobKey.Name, jobKey.Group, DateTime.UtcNow, stopwatch.ElapsedMilliseconds, true, null, null, null, null);
            if (isHidden)
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            // 超時 / 中斷 處理機制
            using var cts = new CancellationTokenSource();
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, cts.Token);
            
            if (maxRunTimeSeconds.HasValue && maxRunTimeSeconds.Value > 0)
            {
                cts.CancelAfter(TimeSpan.FromSeconds(maxRunTimeSeconds.Value));
            }

            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
                exitCode = process.ExitCode;
                isSuccess = true;
                if (exitCode != 0) errorMessage = $"執行程序結束，傳回結束代碼：{process.ExitCode}。";
            }
            catch (TaskCanceledException)
            {
                if (cts.IsCancellationRequested)
                {
                    errorMessage = $"執行時間超過上限 {maxRunTimeSeconds} 秒！觸發強制中斷。";
                    _logger.LogWarning("[{JobKey}] {Msg}", jobKey, errorMessage);
                }
                else
                {
                    errorMessage = "已因新排程觸發且設定為『停止現有的執行個體』而被外部強制中斷。";
                    _logger.LogWarning("[{JobKey}] {Msg}", jobKey, errorMessage);
                }
                
                isSuccess = true; // 將主動的超時中止事件視為一種成功完成但不正常的狀態
                finalEventIdOverride = 328;
                try { process.Kill(true); } catch { } 
                await Task.Delay(100); // 讓執行緒死透、串流關閉
            }

            if (exitCode.HasValue)
            {
                _logger.LogInformation("執行緒 [{JobKey}] 結束，Exit Code: {ExitCode}", jobKey, exitCode);
            }
        }
        catch (Exception ex)
        {
            errorMessage = ex.ToString();
            _logger.LogError(ex, "執行緒 [{JobKey}] 發生未預期的錯誤。", jobKey);
            isSuccess = false;
        }
        finally
        {
            stopwatch.Stop();
            int finalEventId = finalEventIdOverride ?? (isSuccess ? 201 : (errorMessage != null && errorMessage.Contains("強制中斷") ? 328 : 203));
            SaveLog(finalEventId, correlationId, jobKey.Name, jobKey.Group, DateTime.UtcNow, stopwatch.ElapsedMilliseconds, isSuccess, exitCode, stdOutBuilder.ToString(), stdErrBuilder.ToString(), errorMessage);
        }
    }

    private void SaveLog(int eventId, string correlation, string name, string group, DateTime fireTime, long runTime, bool isSuccess, int? exitCode, string? stdOut, string? stdErr, string? error)
    {
        try
        {
            using var conn = new SqliteConnection("Data Source=quartz.db;");
            conn.Open();
            using var cmd = conn.CreateCommand();
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
            
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "無法寫入排程執行日誌。");
        }
    }
}
