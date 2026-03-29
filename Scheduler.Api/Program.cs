using Microsoft.Data.Sqlite;
using Quartz;
using Scheduler.Core.Jobs;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 1. 初始化資料庫
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "quartz.db");
var connString = $"Data Source={dbPath};";

if (!File.Exists(dbPath))
{
    Console.WriteLine("Quartz database not found, initializing...");
    using var connection = new SqliteConnection(connString);
    connection.Open();
    var scriptPath = Path.Combine(builder.Environment.ContentRootPath, "tables_sqlite.sql");
    if (File.Exists(scriptPath))
    {
        var script = File.ReadAllText(scriptPath);
        using var command = connection.CreateCommand();
        command.CommandText = script;
        command.ExecuteNonQuery();
        Console.WriteLine("Quartz SQLite tables created.");
    }
    else
    {
        Console.WriteLine($"WARNING: {scriptPath} not found!");
    }
}

// 確保我們自訂的 Log 資料表存在 (即使 quartz.db 已經被建立了也要檢查)
using (var connection = new SqliteConnection(connString))
{
    connection.Open();
    
    // 開啟 Write-Ahead Logging 模式，大幅提高多環境併發讀寫能力
    using var walCmd = connection.CreateCommand();
    walCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA temp_store=MEMORY;";
    walCmd.ExecuteNonQuery();

    using var logCommand = connection.CreateCommand();
    logCommand.CommandText = @"
        CREATE TABLE IF NOT EXISTS JobExecutionLogs (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            JobName TEXT NOT NULL,
            JobGroup TEXT NOT NULL,
            FireTimeUtc DATETIME NOT NULL,
            RunTimeMs INTEGER NOT NULL,
            IsSuccess INTEGER NOT NULL,
            ExitCode INTEGER,
            StdOut TEXT,
            StdErr TEXT,
            ErrorMessage TEXT,
            CorrelationId TEXT,
            EventId INTEGER
        );";
    logCommand.ExecuteNonQuery();

    // 為特定高頻查詢的查詢與排序建立索引，避免巨量 log 時引發硬碟排序寫入而開啟失敗
    using var indexCmd = connection.CreateCommand();
    indexCmd.CommandText = "CREATE INDEX IF NOT EXISTS IDX_JobExecutionLogs_Group_Name_FireTime ON JobExecutionLogs (JobGroup, JobName, FireTimeUtc DESC);";
    indexCmd.ExecuteNonQuery();

    try
    {
        using var alterCmd1 = connection.CreateCommand();
        alterCmd1.CommandText = "ALTER TABLE JobExecutionLogs ADD COLUMN CorrelationId TEXT;";
        alterCmd1.ExecuteNonQuery();
        using var alterCmd2 = connection.CreateCommand();
        alterCmd2.CommandText = "ALTER TABLE JobExecutionLogs ADD COLUMN EventId INTEGER;";
        alterCmd2.ExecuteNonQuery();
    }
    catch { /* 已經存在的欄位會引發例外，安全忽略 */ }

    // 建立稽核與管理紀錄專用的 AuditLogs
    using var auditCmd = connection.CreateCommand();
    auditCmd.CommandText = @"
        CREATE TABLE IF NOT EXISTS AuditLogs (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            EventId INTEGER NOT NULL,
            EventTimeUtc DATETIME NOT NULL,
            JobName TEXT NOT NULL,
            JobGroup TEXT NOT NULL,
            Description TEXT,
            AccountName TEXT
        );";
    auditCmd.ExecuteNonQuery();
}

// 2. 註冊 Quartz
builder.Services.AddQuartz(q =>
{
    // 如果想要確保我們能在 DI 容器中拿到 Job，可以設定 Concurrent / Scoped
    
    // 使用資料庫持久化
    q.UsePersistentStore(s =>
    {
        s.PerformSchemaValidation = false;
        s.UseProperties = true;
        s.UseMicrosoftSQLite(sqlite =>
        {
            sqlite.ConnectionString = connString;
        });
        s.UseNewtonsoftJsonSerializer();
    });
});

// 3. 註冊 Quartz 的託管服務 (背景執行)
builder.Services.AddQuartzHostedService(opt =>
{
    opt.WaitForJobsToComplete = true; // 關閉程式時等 Job 跑完
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
