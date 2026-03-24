# Quartz.NET 完整使用教學
> 基於 `Quartz_Task_Scheduler_v2` 專案實作，從概念到實戰的逐步說明

---

## 目錄

1. [什麼是 Quartz.NET？](#1-什麼是-quartznet)
2. [專案架構總覽](#2-專案架構總覽)
3. [安裝與初始化](#3-安裝與初始化)
4. [核心概念：Job（工作）](#4-核心概念jobjob工作)
5. [核心概念：Trigger（觸發程序）](#5-核心概念triggertrigger觸發程序)
6. [核心概念：Scheduler（排程器）](#6-核心概念schedulerscheduler排程器)
7. [Cron 表達式完整說明](#7-cron-表達式完整說明)
8. [排程建立與更新：建立 Job + Trigger](#8-排程建立與更新建立-job--trigger)
9. [Trigger 類型詳解](#9-trigger-類型詳解)
10. [Misfire 機制：錯過觸發時該怎麼辦](#10-misfire-機制錯過觸發時該怎麼辦)
11. [並發規則（Concurrency Rule）](#11-並發規則concurrency-rule)
12. [手動觸發與中斷](#12-手動觸發與中斷)
13. [暫停與恢復工作](#13-暫停與恢復工作)
14. [與外部程序整合：ProcessRunnerJob 實作](#14-與外部程序整合processrunnerjob-實作)
15. [超時機制：MaxRunTimeSeconds](#15-超時機制maxruntimeseconds)
16. [執行紀錄（Job Log）與 EventId 設計](#16-執行紀錄job-log與-eventid-設計)
17. [持久化儲存：使用 SQLite](#17-持久化儲存使用-sqlite)
18. [OriginalTriggers：解決讀回 Trigger 設定的難題](#18-originaltriggers解決讀回-trigger-設定的難題)
19. [觸發時機模擬工具（Simulator）](#19-觸發時機模擬工具simulator)
20. [WeeklyInterval：每隔 N 週執行](#20-weeklyinterval每隔-n-週執行)
21. [TriggerDescriptionHelper：人性化排程描述](#21-triggerdescriptionhelper人性化排程描述)
22. [常見錯誤與除錯技巧](#22-常見錯誤與除錯技巧)

---

## 1. 什麼是 Quartz.NET？

Quartz.NET 是一個功能強大的 .NET 開源排程框架，用來取代 Windows 工作排程器。它的三個核心元素是：

| 元素 | 說明 |
|------|------|
| **Job** | 要執行的工作邏輯（實作 `IJob` 介面） |
| **Trigger** | 觸發工作的時間規則（何時、多久觸發一次） |
| **Scheduler** | 排程引擎，管理所有 Job 和 Trigger 的運作 |

**本專案採用 ASP.NET Core Web API + WPF UI** 的架構，以 Quartz.NET 作為核心引擎，實現：
- 新增、編輯、刪除排程任務
- 多種觸發模式（Cron / 週期迴圈 / 單次）
- 跨重開機持久化（SQLite 資料庫）
- 手動觸發、中斷、暫停、恢復

---

## 2. 專案架構總覽

```
Quartz_Task_Scheduler_v2/
├── Scheduler.Api/          # ASP.NET Core Web API (Quartz 引擎宿主)
│   ├── Program.cs          # 啟動程式：Quartz 初始化、資料庫建立
│   └── Controllers/
│       └── JobsController.cs  # REST API：CRUD & 操作排程
│
├── Scheduler.Core/         # 共用函式庫
│   ├── Jobs/
│   │   └── ProcessRunnerJob.cs   # IJob 實作：執行外部程序
│   ├── Models/
│   │   ├── JobInfo.cs            # 工作資訊 DTO
│   │   ├── ScheduleRequest.cs    # 建立/更新排程的請求模型
│   │   ├── TriggerDto.cs         # 觸發程序 DTO
│   │   └── JobLogEntry.cs        # 執行日誌 DTO
│   └── Helpers/
│       └── TriggerDescriptionHelper.cs  # 將 Trigger 轉成中文描述
│
├── Scheduler.Simulator/    # 觸發時機模擬工具（Console App）
│   └── Program.cs
│
└── Scheduler.Ui/           # WPF 桌面介面
```

---

## 3. 安裝與初始化

### 安裝 NuGet 套件

```bash
dotnet add package Quartz
dotnet add package Quartz.Extensions.Hosting
dotnet add package Quartz.Serialization.Newtonsoft
dotnet add package Quartz.Plugins.TimeZoneConverter
dotnet add package Microsoft.Data.Sqlite
```

### 在 `Program.cs` 初始化 Quartz

```csharp
// 1. 初始化 SQLite 資料庫（若不存在則建立）
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "quartz.db");
var connString = $"Data Source={dbPath};";

if (!File.Exists(dbPath))
{
    using var connection = new SqliteConnection(connString);
    connection.Open();
    var script = File.ReadAllText("tables_sqlite.sql");
    using var command = connection.CreateCommand();
    command.CommandText = script;
    command.ExecuteNonQuery();
}

// 2. 註冊 Quartz 服務
builder.Services.AddQuartz(q =>
{
    // 使用 SQLite 持久化儲存
    q.UsePersistentStore(s =>
    {
        s.PerformSchemaValidation = false; // 不驗證 Schema（SQLite 相容性）
        s.UseProperties = true;           // JobDataMap 以字串屬性儲存
        s.UseMicrosoftSQLite(sqlite =>
        {
            sqlite.ConnectionString = connString;
        });
        s.UseNewtonsoftJsonSerializer(); // 序列化格式
    });
});

// 3. 以 Hosted Service 背景執行
builder.Services.AddQuartzHostedService(opt =>
{
    opt.WaitForJobsToComplete = true; // 程式關閉時等 Job 跑完再結束
});
```

> **重點說明**  
> `UseProperties = true` 代表 JobDataMap 的 Key/Value 都以純文字字串存入資料庫，避免序列化問題。所以在程式中讀取時都使用 `GetString()` 方法，再自行轉型。

---

## 4. 核心概念：Job（工作）

### 實作 `IJob` 介面

```csharp
using Quartz;

public class ProcessRunnerJob : IJob
{
    private readonly ILogger<ProcessRunnerJob> _logger;

    // 透過 DI (依賴注入) 取得 Logger
    public ProcessRunnerJob(ILogger<ProcessRunnerJob> logger)
    {
        _logger = logger;
    }

    // 每次排程觸發時，Quartz 會呼叫這個方法
    public async Task Execute(IJobExecutionContext context)
    {
        // context 包含所有執行時期資訊
        var dataMap = context.MergedJobDataMap; // Job + Trigger 的合併資料
        var jobKey = context.JobDetail.Key;      // 工作的唯一識別鍵 (Name + Group)
        
        // 從 JobDataMap 讀取參數
        var fileName = dataMap.GetString("FileName");
        var arguments = dataMap.GetString("Arguments");
        
        // ... 執行業務邏輯
    }
}
```

### `IJobExecutionContext` 常用屬性

| 屬性 | 說明 |
|------|------|
| `context.JobDetail` | 工作的詳細資訊（包含 JobDataMap） |
| `context.Trigger` | 觸發這次執行的 Trigger |
| `context.MergedJobDataMap` | Job + Trigger 的 JobDataMap 合併（Trigger 優先） |
| `context.FireTimeUtc` | 此次觸發的預定時間（UTC） |
| `context.FireInstanceId` | 此次執行的唯一 ID |
| `context.Scheduler` | 排程器本身（可呼叫其他排程操作） |
| `context.CancellationToken` | 中斷信號（被 Interrupt 時會觸發） |

### JobDataMap：傳遞參數給 Job

JobDataMap 是一個鍵值對字典，用來傳遞設定給 Job。本專案使用的參數：

| 參數鍵 | 說明 | 範例值 |
|--------|------|--------|
| `FileName` | 執行檔路徑 | `python.exe` |
| `Arguments` | 執行參數 | `script.py --arg1` |
| `WorkingDirectory` | 工作目錄 | `C:\Scripts` |
| `MaxRunTimeSeconds` | 最長執行秒數 | `300` |
| `ConcurrencyRule` | 並發規則 | `Parallel` |
| `IsHidden` | 是否隱藏視窗 | `True` |
| `IsDisabled` | 是否已停用 | `False` |
| `Author` | 建立者 | `admin` |

---

## 5. 核心概念：Trigger（觸發程序）

Trigger 決定「什麼時候」及「多久執行一次」工作。

### Trigger 的唯一識別鍵

```csharp
var triggerKey = new TriggerKey("觸發程序名稱", "群組名稱");
```

### 三種主要 Trigger 類型

| 類型 | 適用情境 |
|------|---------|
| `SimpleTrigger` | 固定間隔重複（每 N 秒/分/小時），或執行一次 |
| `CronTrigger` | 複雜時間規則（每週五下午三點…） |
| `DailyTimeIntervalTrigger` | 每天在指定時間區間內，每隔 N 分鐘觸發 |

### TriggerDto 資料模型

本專案用 `TriggerDto` 來傳遞觸發設定：

```csharp
public class TriggerDto
{
    public string TriggerName { get; set; } = Guid.NewGuid().ToString();
    public string TriggerGroup { get; set; } = "DefaultGroup";
    public string Description { get; set; } = string.Empty;
    
    // 若有 CronExpression 代表使用 Cron 排程
    public string? CronExpression { get; set; }
    
    // 觸發的起訖時間
    public DateTimeOffset? StartAt { get; set; }
    public DateTimeOffset? EndAt { get; set; }
    
    // 週期迴圈設定
    public int? RepeatInterval { get; set; }        // 間隔數值
    public string? RepeatIntervalUnit { get; set; } // "Second" / "Minute" / "Hour"
    public int? RepeatDuration { get; set; }         // 持續時間數值
    public string? RepeatDurationUnit { get; set; }  // "Minute" / "Hour" / "Day"
    public int? WeeklyInterval { get; set; }         // 每隔幾週觸發（自訂擴充）
    
    // 狀態（唯讀，由 API 填回）
    public string State { get; set; } = string.Empty;
    public DateTime? NextFireTime { get; set; }
    public DateTime? PreviousFireTime { get; set; }
}
```

---

## 6. 核心概念：Scheduler（排程器）

Scheduler 是 Quartz 的核心引擎，負責管理所有 Job 和 Trigger。

### 在 Controller 中取得 Scheduler

```csharp
public class JobsController : ControllerBase
{
    private readonly ISchedulerFactory _schedulerFactory;

    // 透過 DI 注入 ISchedulerFactory
    public JobsController(ISchedulerFactory schedulerFactory)
    {
        _schedulerFactory = schedulerFactory;
    }

    public async Task<IActionResult> SomeAction()
    {
        // 每次從工廠取得 Scheduler 實例
        var scheduler = await _schedulerFactory.GetScheduler();
        
        // ... 使用 scheduler 進行操作
    }
}
```

### 常用 Scheduler API

```csharp
// 查詢
var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup());
var jobDetail = await scheduler.GetJobDetail(jobKey);
var triggers = await scheduler.GetTriggersOfJob(jobKey);
bool exists = await scheduler.CheckExists(jobKey);
var state = await scheduler.GetTriggerState(triggerKey);
var executingJobs = await scheduler.GetCurrentlyExecutingJobs();

// 排程操作
await scheduler.ScheduleJob(job, triggers, replace: true);  // 建立/更新
await scheduler.AddJob(job, replace: true);                  // 不含 Trigger 僅儲存
await scheduler.DeleteJob(jobKey);                           // 刪除
await scheduler.TriggerJob(jobKey, dataMap);                 // 手動立即觸發
await scheduler.Interrupt(jobKey);                           // 中斷執行中的 Job
await scheduler.PauseJob(jobKey);                            // 暫停 (停止觸發)
await scheduler.ResumeJob(jobKey);                           // 恢復觸發
```

---

## 7. Cron 表達式完整說明

Quartz.NET 的 Cron 格式為 **7 個欄位**（比 Linux Cron 多一個秒欄位）：

```
秒 分 時 日 月 星期 [年]
```

| 欄位 | 允許值 | 特殊字元 |
|------|--------|---------|
| 秒 | 0-59 | `, - * /` |
| 分 | 0-59 | `, - * /` |
| 時 | 0-23 | `, - * /` |
| 日 | 1-31 | `, - * ? / L W` |
| 月 | 1-12 或 JAN-DEC | `, - * /` |
| 星期 | 1-7 或 SUN-SAT | `, - * ? / L #` |
| 年（可選） | 空 或 1970-2099 | `, - * /` |

### 特殊字元說明

| 字元 | 意義 | 範例 |
|------|------|------|
| `*` | 任意值 | `*` 在分欄 = 每分鐘 |
| `?` | 不指定（日或星期其中一個必須為 `?`） | `0 0 10 * * ?` |
| `/` | 遞增間隔 | `0/5` 在秒欄 = 從0秒起每5秒 |
| `L` | 最後 | `L` 在日欄 = 每月最後一天 |
| `#` | 第幾個星期幾 | `FRI#3` = 每月第三個星期五 |

### 常用範例

```
# 每天早上 10:00 執行
0 0 10 * * ?

# 每週一到五下午 14:30 執行
0 30 14 ? * MON,TUE,WED,THU,FRI

# 每月1日凌晨 00:00 執行
0 0 0 1 * ?

# 每隔 5 分鐘執行一次（整點起算）
0 0/5 * * * ?

# 每週五下午 3:00
0 0 15 ? * FRI

# 每月最後一天
0 0 10 L * ?

# 每月第三個星期五
0 0 10 ? * FRI#3
```

---

## 8. 排程建立與更新：建立 Job + Trigger

### 完整建立流程

```csharp
[HttpPost]
public async Task<IActionResult> CreateOrUpdateJob([FromBody] ScheduleRequest request)
{
    var scheduler = await _schedulerFactory.GetScheduler();
    var jobKey = new JobKey(request.JobName, request.JobGroup);

    // 若已存在則先刪除（更新操作）
    if (await scheduler.CheckExists(jobKey))
    {
        await scheduler.DeleteJob(jobKey);
    }

    // 建立 JobDetail
    var job = JobBuilder.Create<ProcessRunnerJob>()
        .WithIdentity(jobKey)                          // 設定唯一識別鍵
        .WithDescription(request.Description)          // 描述
        .UsingJobData("FileName", request.FileName)    // 傳入參數
        .UsingJobData("Arguments", request.Arguments)
        .UsingJobData("ConcurrencyRule", request.ConcurrencyRule)
        .StoreDurably()   // 即使沒有 Trigger 也保留 Job
        .Build();

    // 建立 Trigger（可多個）
    var triggersToSchedule = new HashSet<ITrigger>();
    foreach (var tReq in request.Triggers)
    {
        var trigger = BuildTrigger(tReq, request);
        triggersToSchedule.Add(trigger);
    }

    // 排程
    if (triggersToSchedule.Count == 0)
        await scheduler.AddJob(job, true);          // 只儲存 Job，沒有觸發
    else
        await scheduler.ScheduleJob(job, triggersToSchedule, replace: true);

    return Ok(new { Message = "排程建立或更新成功" });
}
```

> **`StoreDurably()` 說明**  
> 若不加這個，當所有 Trigger 都失效（過期）後，Job 本身也會被 Quartz 自動刪除。  
> 使用 `StoreDurably()` 讓 Job 即使沒有 Trigger 也能被保留。

### JobKey 與 TriggerKey：唯一識別鍵

```csharp
// Job 使用 Name + Group 做唯一識別
var jobKey = new JobKey("每日備份", "系統維護");

// Trigger 同樣使用 Name + Group
var triggerKey = new TriggerKey("每日備份觸發", "系統維護");
```

---

## 9. Trigger 類型詳解

### 9.1 CronTrigger：Cron 表達式觸發

適用於複雜的時間規則（每天特定時間、每週幾等）。

```csharp
var trigger = TriggerBuilder.Create()
    .WithIdentity(new TriggerKey("t1", "default"))
    .StartAt(startAt)            // 開始時間（可省略，預設立即）
    .EndAt(endAt)                // 結束時間（可省略）
    .WithCronSchedule("0 30 10 * * ?", x =>
    {
        // Misfire 處理（見第 10 章）
        x.WithMisfireHandlingInstructionFireAndProceed();
    })
    .Build();
```

### 9.2 SimpleTrigger：固定間隔重複

適用於「每 N 分鐘重複執行」的需求。

```csharp
var trigger = TriggerBuilder.Create()
    .WithIdentity(new TriggerKey("t2", "default"))
    .StartAt(startAt)
    .WithSimpleSchedule(x =>
    {
        x.WithIntervalInMinutes(5)  // 每 5 分鐘
         .RepeatForever();          // 無限重複（或用 .WithRepeatCount(N) 指定次數）
        
        x.WithMisfireHandlingInstructionFireNow();
    })
    .Build();

// 執行一次
var onceOnly = TriggerBuilder.Create()
    .WithSimpleSchedule(x => x.WithRepeatCount(0))
    .Build();
```

### 9.3 DailyTimeIntervalTrigger：每日時間區間觸發

適用於「每天 10:00 到 12:00 之間，每 5 分鐘觸發一次」的需求。

```csharp
var trigger = TriggerBuilder.Create()
    .WithIdentity(new TriggerKey("t3", "default"))
    .WithDailyTimeIntervalSchedule(x =>
    {
        x.WithIntervalInMinutes(5)
         .StartingDailyAt(new TimeOfDay(10, 0, 0))  // 每天 10:00 開始
         .EndingDailyAt(new TimeOfDay(12, 0, 0))    // 到 12:00 結束
         .OnDaysOfTheWeek(DayOfWeek.Monday, DayOfWeek.Friday) // 僅週一、週五
         .WithMisfireHandlingInstructionFireAndProceed();
    })
    .Build();
```

### 9.4 本專案的智慧轉譯邏輯

本專案在 API Controller 中實作了自動判斷邏輯：

```csharp
/*
 * 情境1：純 Cron（每天/每週特定時間，不重複）
 *   → 使用 CronSchedule
 *
 * 情境2：Cron + 重複間隔（每天10:00起，每5分鐘觸發一次）
 *   → 轉換為 DailyTimeIntervalSchedule
 *   → 因為 DailyTimeIntervalTrigger 可以同時設定「起始時間」和「重複間隔」
 *
 * 情境3：純重複間隔（沒有特定時間，單純每N分鐘）
 *   → 使用 SimpleSchedule
 *
 * 情境4：單次執行
 *   → 使用 SimpleSchedule + RepeatCount(0)
 */
```

---

## 10. Misfire 機制：錯過觸發時該怎麼辦

**Misfire（超時觸發）** 發生在：排程器停擺（如程式重啟、當機）後，有應該執行但沒執行到的觸發點。

### 兩種主要處理策略

#### `FireAndProceed`：補跑一次後繼續正常排程

```csharp
// CronTrigger
x.WithMisfireHandlingInstructionFireAndProceed();

// DailyTimeIntervalTrigger
x.WithMisfireHandlingInstructionFireAndProceed();

// SimpleTrigger
x.WithMisfireHandlingInstructionFireNow();
```

> **行為**：若錯過了 3 個觸發點，只補執行 1 次，然後從下一個正常時間點繼續。

#### `DoNothing`：直接跳過，從下一個正常觸發點繼續

```csharp
// CronTrigger
x.WithMisfireHandlingInstructionDoNothing();

// SimpleTrigger
x.WithMisfireHandlingInstructionNextWithExistingCount();
```

> **行為**：完全忽略錯過的觸發點，不補執行。

### 在本專案中的選擇

```csharp
// 在 ScheduleRequest 中以 bool 參數控制
if (request.MisfireActionFireAndProceed)
    x.WithMisfireHandlingInstructionFireAndProceed();
else
    x.WithMisfireHandlingInstructionDoNothing();
```

---

## 11. 並發規則（Concurrency Rule）

**並發問題**：若上一個工作還在跑，下一個觸發時間點又到了，會發生什麼事？

本專案實作了三種規則，在 `ProcessRunnerJob.Execute()` 中處理：

```csharp
var concurrencyRule = dataMap.GetString("ConcurrencyRule") ?? "Parallel";

// 取得目前正在執行的同一個 Job 的其他實例
var executingJobs = await context.Scheduler.GetCurrentlyExecutingJobs();
var otherInstances = executingJobs
    .Where(x => x.JobDetail.Key.Equals(jobKey) 
             && x.FireInstanceId != context.FireInstanceId)
    .ToList();

if (otherInstances.Any())
{
    if (concurrencyRule == "DoNotStart")
    {
        // 規則1：不啟動新實例，直接略過本次觸發
        _logger.LogInformation("Job {JobKey} aborted (Rule: DoNotStart).", jobKey);
        return;
    }
    else if (concurrencyRule == "StopExisting")
    {
        // 規則2：強制中斷現有實例，啟動新的
        foreach (var inst in otherInstances)
        {
            await context.Scheduler.Interrupt(inst.FireInstanceId);
        }
        // 接著繼續往下執行...
    }
    // 若為 "Parallel"：直接往下，讓兩個實例同時運行
}
```

### 三種規則比較

| 規則 | 值 | 行為 | 適用情境 |
|------|-----|------|---------|
| 平行多開 | `Parallel` | 新舊實例同時執行，互不干擾 | 工作彼此獨立，執行時間短 |
| 不要啟動 | `DoNotStart` | 若有實例在跑就跳過本次 | 工作不可重複執行（如資料同步）|
| 停止現有 | `StopExisting` | 中斷舊實例，啟動新的 | 需要保持「最新版本」執行 |

> **注意**：Quartz 本身提供 `[DisallowConcurrentExecution]` Attribute，但本專案選擇**手動實作**，以支援更細緻的三種規則。

---

## 12. 手動觸發與中斷

### 手動立即觸發

```csharp
[HttpPost("{group}/{name}/trigger")]
public async Task<IActionResult> TriggerJob(string group, string name)
{
    var scheduler = await _schedulerFactory.GetScheduler();
    var jobKey = new JobKey(name, group);
    
    if (!await scheduler.CheckExists(jobKey)) return NotFound();
    
    // 手動觸發：傳入額外的 JobDataMap，讓 Job 知道這是手動觸發
    await scheduler.TriggerJob(jobKey, new JobDataMap
    {
        { "TriggerReason", "Manual" }
    });
    
    return Ok(new { Message = "已觸發。" });
}
```

在 `ProcessRunnerJob` 中辨別手動觸發：

```csharp
// MergedJobDataMap 會將 TriggerJob 傳入的 DataMap 合併進來
bool isManual = context.MergedJobDataMap.ContainsKey("TriggerReason") 
    && context.MergedJobDataMap.GetString("TriggerReason") == "Manual";

// 可據此記錄不同的 EventId 來區分觸發來源
int triggerEventId = isManual ? 110 : 107;
```

### 中斷執行中的 Job

```csharp
[HttpPost("{group}/{name}/interrupt")]
public async Task<IActionResult> InterruptJob(string group, string name)
{
    var scheduler = await _schedulerFactory.GetScheduler();
    var jobKey = new JobKey(name, group);
    
    // Interrupt 會設定 context.CancellationToken 的取消信號
    bool interrupted = await scheduler.Interrupt(jobKey);
    
    return Ok(new { Message = interrupted ? "已傳送結束訊號。" : "工作可能並未執行。" });
}
```

在 `ProcessRunnerJob` 中處理中斷信號：

```csharp
// 將 Quartz 的 CancellationToken 與自訂的超時 Token 連結
using var cts = new CancellationTokenSource();
using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
    context.CancellationToken, cts.Token);

try
{
    await process.WaitForExitAsync(linkedCts.Token);
}
catch (TaskCanceledException)
{
    // 中斷信號觸發
    try { process.Kill(true); } catch { }
    await Task.Delay(100); // 等待程序完全結束
}
```

---

## 13. 暫停與恢復工作

### 暫停 Job（停止所有觸發）

```csharp
[HttpPost("{group}/{name}/pause")]
public async Task<IActionResult> PauseJob(string group, string name)
{
    var scheduler = await _schedulerFactory.GetScheduler();
    var jobKey = new JobKey(name, group);

    // 在 JobDataMap 記錄「已停用」狀態（便於 UI 讀取）
    var detail = await scheduler.GetJobDetail(jobKey);
    if (detail != null)
    {
        detail.JobDataMap.Put("IsDisabled", "True");
        await scheduler.AddJob(detail, true, true); // 更新 JobDetail
    }

    // 暫停 Job 的所有 Trigger
    await scheduler.PauseJob(jobKey);
    return Ok(new { Message = "排程已暫停" });
}
```

### 恢復 Job

```csharp
[HttpPost("{group}/{name}/resume")]
public async Task<IActionResult> ResumeJob(string group, string name)
{
    var scheduler = await _schedulerFactory.GetScheduler();
    var jobKey = new JobKey(name, group);

    var detail = await scheduler.GetJobDetail(jobKey);
    if (detail != null)
    {
        detail.JobDataMap.Put("IsDisabled", "False");
        await scheduler.AddJob(detail, true, true);
    }

    await scheduler.ResumeJob(jobKey);
    return Ok(new { Message = "排程已恢復" });
}
```

### Trigger 狀態說明

| TriggerState | 本專案顯示文字 | 說明 |
|------|------|------|
| `Normal` | 準備就緒 | 正常等待觸發 |
| `Paused` | 已停用 | 被暫停，不會觸發 |
| `Blocked` | 執行中 | Job 正在執行 |
| `Complete` | 完成 | Trigger 已完成所有觸發 |
| `Error` | 發生錯誤 | 發生錯誤 |
| `None` | 準備就緒 | 不存在或正常 |

---

## 14. 與外部程序整合：ProcessRunnerJob 實作

本專案的 Job 核心功能是執行外部程序（.exe、.py、.bat 等）。

### 兩種執行模式

#### 隱藏模式（`IsHidden = true`）

```csharp
var processStartInfo = new ProcessStartInfo
{
    FileName = fileName,
    Arguments = arguments,
    WorkingDirectory = workingDirectory,
    UseShellExecute = false,        // 不用 Shell
    CreateNoWindow = true,          // 不建立視窗
    RedirectStandardOutput = true,  // 捕捉 STDOUT
    RedirectStandardError = true    // 捕捉 STDERR
};

// 非同步讀取輸出，避免緩衝區死鎖
process.OutputDataReceived += (sender, e) =>
{
    if (!string.IsNullOrEmpty(e.Data))
        stdOutBuilder.AppendLine(e.Data);
};
process.Start();
process.BeginOutputReadLine();
process.BeginErrorReadLine();
```

#### 可見模式（`IsHidden = false`）

```csharp
var processStartInfo = new ProcessStartInfo
{
    FileName = fileName,
    Arguments = arguments,
    WorkingDirectory = workingDirectory,
    UseShellExecute = true,    // 使用 Shell 開啟（可彈出終端視窗）
    CreateNoWindow = false     // 顯示視窗
    // 不能 Redirect 輸出（UseShellExecute = true 不支援）
};
```

### 工作目錄與路徑解析

```csharp
// 若 FileName 只是檔名（不含路徑分隔符），
// 且設定了 WorkingDirectory，則自動組合絕對路徑
if (!string.IsNullOrWhiteSpace(workingDirectory) 
    && !fileName.Contains('\\') 
    && !fileName.Contains('/'))
{
    string combinedPath = Path.Combine(workingDirectory, fileName);
    if (File.Exists(combinedPath))
    {
        fileName = combinedPath; // 改為絕對路徑
    }
}
```

---

## 15. 超時機制：MaxRunTimeSeconds

當程序執行時間過長時，可設定自動強制中止。

```csharp
// 從 JobDataMap 讀取超時設定
int? maxRunTimeSeconds = int.TryParse(
    dataMap.GetString("MaxRunTimeSeconds"), out var v) ? v : null;

// 建立兩個 CancellationToken 的聯合：
// 1. context.CancellationToken：外部 Interrupt 信號
// 2. cts.Token：超時自動取消信號
using var cts = new CancellationTokenSource();
using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
    context.CancellationToken, cts.Token);

// 設定超時計時器
if (maxRunTimeSeconds.HasValue && maxRunTimeSeconds.Value > 0)
{
    cts.CancelAfter(TimeSpan.FromSeconds(maxRunTimeSeconds.Value));
}

try
{
    await process.WaitForExitAsync(linkedCts.Token);
}
catch (TaskCanceledException)
{
    // 判斷是超時還是外部中斷
    if (cts.IsCancellationRequested)
        errorMessage = $"執行時間超過上限 {maxRunTimeSeconds} 秒！觸發強制中止。";
    else
        errorMessage = "已因外部強制中斷。";
    
    // 強制終止程序
    try { process.Kill(true); } catch { }
    await Task.Delay(100); // 等待程序釋放資源
}
```

---

## 16. 執行紀錄（Job Log）與 EventId 設計

本專案使用 **EventId 事件識別碼** 來精確描述執行過程中的每個階段。

### EventId 對照表

| EventId | 說明 |
|---------|------|
| `107` | 排程觸發（自動排程啟動） |
| `110` | 手動觸發（使用者手動執行） |
| `129` | 已建立工作處理程序 |
| `100` | 工作已開始 |
| `200` | 動作已啟動（程序開始跑） |
| `201` | ✅ 成功完成 |
| `203` | ❌ 執行失敗（錯誤或無效參數） |
| `322` | ⚠️ 因並發規則略過 |
| `328` | ⚠️ 被外部強制中斷 |

### SQLite 寫入紀錄

```csharp
private void SaveLog(int eventId, string correlation, string name, string group,
    DateTime fireTime, long runTime, bool isSuccess, 
    int? exitCode, string? stdOut, string? stdErr, string? error)
{
    using var conn = new SqliteConnection("Data Source=quartz.db;");
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        INSERT INTO JobExecutionLogs 
        (JobName, JobGroup, FireTimeUtc, RunTimeMs, IsSuccess, ExitCode, 
         StdOut, StdErr, ErrorMessage, CorrelationId, EventId) 
        VALUES (@Name, @Group, @FireTime, @RunTime, @IsSuccess, @ExitCode, 
                @StdOut, @StdErr, @Error, @CorrId, @EventId)";
    
    cmd.Parameters.AddWithValue("@Name", name);
    cmd.Parameters.AddWithValue("@CorrId", correlation); // 同一次執行的唯一 ID
    cmd.Parameters.AddWithValue("@EventId", eventId);
    // ... 其他參數
    cmd.ExecuteNonQuery();
}
```

### 查詢最後執行結果

```csharp
// 從日誌中取得每個 Job 的最後執行結果
cmd.CommandText = @"SELECT JobGroup, JobName, ExitCode, IsSuccess, ErrorMessage, FireTimeUtc 
                    FROM JobExecutionLogs ORDER BY FireTimeUtc DESC, Id DESC";

// 只取每個 Job 的最新一筆
while (reader.Read())
{
    string key = $"{reader.GetString(0)}::{reader.GetString(1)}";
    if (!lastRunResults.ContainsKey(key))  // 只保留最新的
    {
        bool isSuccess = reader.GetInt32(3) == 1;
        // ... 組合狀態文字
        lastRunResults[key] = isSuccess ? $"成功執行 ({exitCode})" : "執行失敗";
    }
}
```

---

## 17. 持久化儲存：使用 SQLite

Quartz 的 `UsePersistentStore` 設定會自動將排程資料（Job、Trigger）存入資料庫，重新啟動後自動恢復。

### 資料庫初始化

```csharp
// tables_sqlite.sql 是 Quartz 官方提供的 SQLite 建表腳本
// 包含 QRTZ_JOB_DETAILS, QRTZ_TRIGGERS 等多個資料表
var script = File.ReadAllText("tables_sqlite.sql");
using var command = connection.CreateCommand();
command.CommandText = script;
command.ExecuteNonQuery();
```

### 自訂 Log 資料表（額外建立）

```sql
CREATE TABLE IF NOT EXISTS JobExecutionLogs (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    JobName         TEXT NOT NULL,
    JobGroup        TEXT NOT NULL,
    FireTimeUtc     DATETIME NOT NULL,
    RunTimeMs       INTEGER NOT NULL,
    IsSuccess       INTEGER NOT NULL,
    ExitCode        INTEGER,
    StdOut          TEXT,
    StdErr          TEXT,
    ErrorMessage    TEXT,
    CorrelationId   TEXT,
    EventId         INTEGER
);
```

### 安全的欄位新增（相容舊資料）

```csharp
// 若新版本新增了欄位，舊資料庫可能沒有
// 用 try/catch 安全地 ALTER TABLE
try
{
    using var alterCmd = connection.CreateCommand();
    alterCmd.CommandText = "ALTER TABLE JobExecutionLogs ADD COLUMN CorrelationId TEXT;";
    alterCmd.ExecuteNonQuery();
}
catch { /* 欄位已存在，安全忽略 */ }
```

---

## 18. OriginalTriggers：解決讀回 Trigger 設定的難題

### 問題背景

Quartz 在持久化 Trigger 時，會轉換成自己的格式（Cron 或 DailyTimeInterval）。  
讀回時，複合的設定參數（如 `RepeatInterval`、`WeeklyInterval`）難以完整還原。

### 解決方案：序列化 TriggerDto 到 JobDataMap

```csharp
// 建立 Job 時，將完整的 Trigger 設定序列化存入 JobDataMap
var job = JobBuilder.Create<ProcessRunnerJob>()
    .UsingJobData("OriginalTriggers", 
        JsonSerializer.Serialize(request.Triggers)) // 保存完整設定
    .Build();
```

```csharp
// 讀取時，從 JobDataMap 還原
List<TriggerDto> originalTriggers = new();
if (detail.JobDataMap.ContainsKey("OriginalTriggers"))
{
    originalTriggers = JsonSerializer.Deserialize<List<TriggerDto>>(
        detail.JobDataMap.GetString("OriginalTriggers")!);
}

// 將原始設定與即時狀態（NextFireTime、State）合併
foreach (var origT in originalTriggers)
{
    var liveT = triggers.FirstOrDefault(
        x => x.Key.Name == origT.TriggerName && x.Key.Group == origT.TriggerGroup);
    
    if (liveT != null)
    {
        var state = await scheduler.GetTriggerState(liveT.Key);
        origT.State = state switch { TriggerState.Normal => "準備就緒", ... };
        origT.NextFireTime = liveT.GetNextFireTimeUtc()?.LocalDateTime;
    }
    
    jobInfo.Triggers.Add(origT);
}
```

---

## 19. 觸發時機模擬工具（Simulator）

本專案包含一個 Console 工具，可以預先推演「未來 N 天內的所有觸發點」以及「並發衝突情況」，**不需要真正啟動排程**。

### 核心：`TriggerUtils.ComputeFireTimes`

```csharp
// TriggerUtils 是 Quartz 內建的工具，可計算未來的觸發時間
var fireTimes = TriggerUtils.ComputeFireTimes(
    (IOperableTrigger)trigger, null, 10000)  // 最多計算 10000 個觸發點
    .Where(t => t <= toTime && t >= startAt)
    .ToList();
```

### 使用方式

1. 修改 `Scheduler.Simulator/Program.cs` 最上方的參數設定區
2. 執行 `dotnet run`
3. 觀察時間軸推演結果

```csharp
// 參數設定區（直接修改這裡）
DateTimeOffset startAt = DateTimeOffset.Now.Date.AddHours(10); // 早上 10:00
string cronExpression = "0 0 10 * * ?";  // 每天 10:00
int repeatInterval = 5;          // 觸發後每 5 分鐘重複
string repeatUnit = "Minute";
int repeatDuration = 2;          // 持續 2 小時
string repeatDurationUnit = "Hour";
string concurrencyRule = "DoNothing"; // 並發規則
TimeSpan jobExecutionDuration = TimeSpan.FromMinutes(3); // 假設每次跑 3 分鐘
int simulateDays = 2;            // 推演未來 2 天
```

### 輸出範例

```
[目前參數設定]
- 排程類型: 週期性排程 (Cron/Daily)
- Cron表達式: 0 0 10 * * ?
- 重複執行: 每隔 5 Minute
- 持續時間: 2 Hour
- 工作耗時: 3 分鐘
- 並發規則: DoNothing
- 模擬區間: 從 2026/03/25 10:00:00 開始算起 2 天內

=> ✅ Quartz 共推算出 49 個預定觸發點。

============= [時間軸情境推演] =============
[03/25 10:00:00] - 觸發信號抵達: 正常啟動 (預計執行 3 分鐘至 10:03:00 結束)
[03/25 10:05:00] - 觸發信號抵達: 依並發規則略過 (因為上一輪的程序仍在執行中)
[03/25 10:10:00] - 觸發信號抵達: 正常啟動 (預計執行 3 分鐘至 10:13:00 結束)
...
```

---

## 20. WeeklyInterval：每隔 N 週執行

這是本專案的**自訂擴充**功能，Quartz 原生不支援「每隔 N 週執行」，由 Job 執行邏輯自行判斷並跳過。

### 原理

```csharp
// 在 ProcessRunnerJob.Execute() 中：
if (context.Trigger.JobDataMap.ContainsKey("WeeklyInterval"))
{
    if (int.TryParse(context.Trigger.JobDataMap.GetString("WeeklyInterval"), 
        out int wInt) && wInt > 1)
    {
        // 計算「從 Trigger 啟動日的那一週」到「今天那一週」共過了幾週
        var startDt = context.Trigger.StartTimeUtc.LocalDateTime.Date;
        var nowDt = DateTimeOffset.UtcNow.LocalDateTime.Date;
        var startWeekStart = startDt.AddDays(-(int)startDt.DayOfWeek); // 該週的週日
        var currentWeekStart = nowDt.AddDays(-(int)nowDt.DayOfWeek);
        int weeksPassed = (int)Math.Round(
            (currentWeekStart - startWeekStart).TotalDays / 7.0);
        
        // 若不是「第 N 週的倍數」，就放棄這次執行
        if (weeksPassed % wInt != 0)
        {
            _logger.LogInformation("Job skipped (WeeklyInterval = {wInt}).", wInt);
            return;
        }
    }
}
```

### 實作邏輯

- 排程本身設定為「每週」執行（正常 Cron）
- Job 在執行時計算「距離啟動日已過幾週」
- 若不符合倍數條件，直接 `return` 不執行
- 此方式讓 Quartz 的 NextFireTime 仍然顯示為「下一個週觸發點」

---

## 21. TriggerDescriptionHelper：人性化排程描述

`TriggerDescriptionHelper` 將 `TriggerDto` 轉換為人類可讀的中文描述，用於 UI 顯示。

### `GetDescription` 輸出範例

| Cron 表達式 | RepeatInterval | 輸出描述 |
|-------------|----------------|---------|
| `0 30 10 * * ?` | 無 | `於每天 上午 10:30:00` |
| `0 0 9 ? * MON,FRI` | 5 分鐘 | `從 2026/3/25 開始，每個星期的 星期一、星期五 的 上午 09:00:00 - 觸發之後，每 5 分鐘便重複一次。` |
| `0 0 0 1 * ?` | 無 | `每個月份的 第 1 天 的 上午 00:00:00` |
| 空字串 | 無 | `於 2026/3/25 上午 10:00:00 執行一次` |

### `GetTriggerType` 輸出

| 條件 | 輸出 |
|------|------|
| 無 CronExpression | `僅一次` |
| DOM 欄位為 `1/*` 或 `*/?` | `每天` |
| DOW 欄位有指定星期 | `每週` |
| DOM 欄位為 `1` | `每月` |
| 其他 | `自訂排程` |

---

## 22. 常見錯誤與除錯技巧

### ❌ Trigger 沒有觸發

**可能原因：**
- `StartAt` 設定為過去的時間，但 Misfire 策略設為 `DoNothing`
- `EndAt` 已過期
- Job 被暫停（`PauseJob` 後忘記 `ResumeJob`）
- SQLite 資料庫鎖定

**排查方式：**
```csharp
var state = await scheduler.GetTriggerState(triggerKey);
// 若為 TriggerState.Paused，代表已暫停
// 若為 TriggerState.Complete，代表時間窗口已結束

var nextFire = trigger.GetNextFireTimeUtc();
// 若為 null，代表不會再觸發
```

### ❌ `UseShellExecute` 衝突錯誤

**錯誤訊息**：`RedirectStandardOutput cannot be used when UseShellExecute is true.`

**解決**：`RedirectStandardOutput/Error` 必須在 `UseShellExecute = false` 時才能使用。

```csharp
// 正確：隱藏模式
processStartInfo.UseShellExecute = false;
processStartInfo.RedirectStandardOutput = true; // ✅

// 錯誤：顯示模式時不能 Redirect
processStartInfo.UseShellExecute = true;
processStartInfo.RedirectStandardOutput = true; // ❌ 會互相衝突
```

### ❌ JobDataMap 讀取失敗

由於 `UseProperties = true`，所有值都以字串儲存，讀取時需手動轉型：

```csharp
// ❌ 錯誤：直接 GetBoolean 可能失敗
bool isHidden = dataMap.GetBoolean("IsHidden");

// ✅ 正確：先取字串，再手動轉型
bool isHidden = bool.TryParse(dataMap.GetString("IsHidden"), out var h) && h;
```

### ❌ DailyTimeIntervalTrigger 跨日設定

若 `RepeatDuration` 設定後計算的結束時間跨越午夜（00:00），需截斷至 `23:59:59`：

```csharp
if (endDt.Date > startDt.Date)
    x.EndingDailyAt(new TimeOfDay(23, 59, 59));  // 當天最晚可到
else
    x.EndingDailyAt(new TimeOfDay(endDt.Hour, endDt.Minute, endDt.Second));
```

### 🔧 使用 Simulator 除錯觸發邏輯

在修改排程設定前，先到 `Scheduler.Simulator/Program.cs` 設定相同的參數，執行模擬，確認觸發落點符合預期，再到實際專案套用。

---

## 附錄：JobExecutionLogs 資料庫結構

| 欄位 | 類型 | 說明 |
|------|------|------|
| `Id` | INTEGER PK | 自動遞增識別碼 |
| `JobName` | TEXT | 工作名稱 |
| `JobGroup` | TEXT | 工作群組 |
| `FireTimeUtc` | DATETIME | 觸發時間（UTC） |
| `RunTimeMs` | INTEGER | 執行耗時（毫秒） |
| `IsSuccess` | INTEGER | 是否成功（0/1） |
| `ExitCode` | INTEGER | 程序結束代碼（可為 NULL） |
| `StdOut` | TEXT | 標準輸出（隱藏模式才有） |
| `StdErr` | TEXT | 標準錯誤（隱藏模式才有） |
| `ErrorMessage` | TEXT | 錯誤訊息（可為 NULL） |
| `CorrelationId` | TEXT | 同一次執行的唯一關聯 ID |
| `EventId` | INTEGER | 事件識別碼（見第 16 章） |

---

*文件產生時間：2026-03-25 | 基於 Quartz_Task_Scheduler_v2 專案實作*
