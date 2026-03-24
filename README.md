# Quartz Task Scheduler v2

專為取代 Windows 內建「工作排程器」而量身打造的現代化排程管理系統。本專案採用前後端分離 (Client-Server) 的架構，結合了 Quartz.NET 強大的背景作業引擎、高效的 ASP.NET Core REST API，以及原生流暢的 Windows 11 Fluent Design (WPF-UI) 使用者介面。

## 🌟 核心特色 (與 v2 最新升級)

本系統在還原絕大部分 Windows 工作排程器日常體驗的同時，針對維運痛點提供了強大的擴充與改良功能：

* **原生 Fluent 現代化美學**：採用最新 WPF-UI 框架建置，完整支援 Windows 11 Mica 半透明材質、圓角視窗及動態深色主題，提供前所未有的視覺體驗。
* **API 與 UI 分離架構**：背景排程引擎獨立為 ASP.NET Web API 伺服器，UI 客戶端透過 REST API 與其對接。不僅可以實現在地管理，更完美支援 **遠端管理** 及 **跨伺服器部署**。為了因應企業嚴格的內網環境，連線層已全面**解除系統預設 Proxy 綁定**，確保暢通無阻。
* **進階的重複間隔與持續時間**：不僅支援精確到「分鐘、小時」的彈性重複執行間隔，現在更完美實裝了媲美原生系統的**「持續時間 (Duration)」**！系統會自動幫您精準換算每週/每日的當班時段，安全下達停止指令而不會跨日干擾。
* **永世留存的歷史快照與精準溯源**：打破底層排程引擎會將「過期/僅一次」排程從資料庫抹除的限制，本系統會動態攔截並將排程的**原始 JSON 參數快照**永久封裝進資料庫。更深度區分了「使用者手動觸發」與「排程器自動執行」的日誌標籤；即使遇到衝突，狀態轉譯也極具指標性 (如：「依並發規則略過」)。未來無論如何修改排程設定，歷史軌跡永遠忠實保留！
* **無痛可攜式自動部署**：程式啟動時會自動偵測並生成所需的 Quartz 引擎與紀錄 SQLite 資料表，發佈資料夾可直接隨身帶著走，免安裝龐大資料庫。

## 🛠️ 技術堆疊 (Tech Stack)

### 後端排程引擎 (Scheduler.Api) & 核心模組 (Scheduler.Core)
* **核心框架**: .NET 8.0 ASP.NET Core Web API
* **排程引擎**: [Quartz.NET](https://www.quartz-scheduler.net/) 3.16.1
* **資料庫介接**: SQLite (Microsoft.Data.Sqlite)
* **文件與測試**: Swagger OpenAPI
* **關鍵功能**: 透過 `JobDataMap` 進行依賴注入、狀態保持，以及 `IJob` (ProcessRunnerJob) 的自訂非同步啟動 (ShellExecute) 與超時管理邏輯。

### 前端使用者介面 (Scheduler.Ui)
* **核心框架**: .NET 8.0 WPF (Windows Presentation Foundation)
* **UI 函式庫**: [WPF-UI](https://wpfui.lepo.co/) 4.2.0 (負責 Fluent Design 元件流暢渲染)
* **架構模式**: MVVM (Model-View-ViewModel)，使用 [CommunityToolkit.Mvvm](https://learn.microsoft.com/zh-tw/dotnet/communitytoolkit/mvvm/) 進行資料繫結與命令控制。
* **通訊協定**: `HttpClient` 封裝 Json REST API 請求 (`SchedulerApiService`)
### 沙盤推演與除錯輔助 (Scheduler.Simulator)
* **核心框架**: .NET 10.0 主控台應用程式
* **時間軸預測工具**: 專案隨附獨立的 CLI 模擬測試模組。開發與維運人員可自由填入想定參數，程式將直接呼叫 Quartz 底層核心 `ComputeFireTimes` 繪製未來百次落點，並生動推演「並發規則」發生碰撞時的時間軸結果。
## 🚀 系統功能詳解

### 1. 進階工作設定 (Job Configuration)
* **隱藏目標程式視窗**：對應原生功能的隱藏執行模式，支援背景暗中執行 Batch 或 CLI 工具，不干擾使用者桌面 (NoWindow)。
* **最大執行時間限制 (Max Run Time)**：直覺化提供「分鐘、小時、天」等多種單位防呆介面，精準換算為 Timeout 權杖，強力斬斷因未預期狀況死當的執行緒。
* **並發衝突規則 (Concurrency Rules)**：
  * `不要啟動新執行個體`：(預設值) 若前一輪作業尚未完成，即放棄本次觸發，防止資源打架。
  * `以平行方式執行新執行個體`：無限制地允許相同腳本同時間多開。
  * `停止現有的執行個體`：強制砍掉舊且卡住的進程，強行啟動新的一輪。
* **永久作者屬性**：創建排程當下自動綁定當前網域的 `UserName`，永久封存紀錄不會因為更換電腦而丟失。

### 2. 靈活的觸發程序 (Triggers)
支援直覺化 GUI 設定，並經由系統底層動態編譯轉換為精準的 Cron 表達式 (Cron Expression) 或簡單排程 (Simple Schedule)：
* **僅一次 / 每天**：可自訂指定時間點、或每隔 X 天絕對循環執行一次。
* **每週**：可絕對自訂每隔 X 週，並自由複選單週內「星期一到星期日」的任意天數執行。
* **每月**：支援雙模式：
  * 絕對日期：例如每月勾選的這幾天執行。
  * 相對日期：例如每個有勾選的「第一、二、三、四個」星期幾執行。

### 3. 可客製化的網路連接埠 (Custom Port Routing)
若需部署到自訂網段或遷移伺服器，設定皆全面開放於本地配置檔：
* **API 端**: 透過修改 API 根目錄下的 `appsettings.json` 中 `Kestrel:Endpoints:Http:Url` 區塊即可重綁伺服器監聽的 IP 或 Port (預設 `http://localhost:5196`)。
* **UI 端**: 修改 UI 根目錄下的 `appsettings.json` 中 `SchedulerApi:BaseUrl`，UI 啟動時即會動態掛載並連線至新的遠端 API，全程跨機器免重新編譯。

## 💻 開發與建置說明

本方案採用多層級架構方案設計 (`.sln`)，包含 `Scheduler.Api`, `Scheduler.Ui`, 以及共用資料實體層的 `Scheduler.Core`。

1. **設定啟動專案**：在 Visual Studio 中，您必須設定「多重啟動專案」，「同時」將 `Scheduler.Api`（作為背景伺服器）與 `Scheduler.Ui` 啟動才能擁有完整的端到端連線體驗。
2. **資料庫無聲安裝 (Initialization)**：第一次啟動 API 伺服器時，程式碼層的 `Program.cs` 會自動在同級目錄下尋找 `.sql` 指令碼建立 Quartz 排程表，並同步建立獨立的 `JobExecutionLogs` 表格。該 `.db` 預設生成於 API 程式根目錄。
3. **發行規則 (Publish)**：`tables_sqlite.sql` 及 `appsettings.json` 已在 `.csproj` 專案檔中被設定為 `<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>`，使用 `dotnet publish` 發行時會自動打包設定檔與結構腳本。發佈資料夾可直接整包複製到全新未裝過任何軟體的 Windows 主機上！

---
*維護人員須知：未來若升級 Quartz.NET 大版號，請關注官方 SQLite Table Schema 腳本是否有改版或需要遷移 (Migration)，以免造成排程讀取錯誤。*
