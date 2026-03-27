using Scheduler.Core.Models;
using System.Windows;
using System;

namespace Scheduler.Ui.ViewModels;

public class SystemLogEntryViewModel
{
    public AuditLogEntry Original { get; }
    public SystemLogEntryViewModel(AuditLogEntry entry) => Original = entry;

    public string EventTime => Original.EventTimeUtc.ToLocalTime().ToString("yyyy/M/d tt hh:mm:ss");
    
    public string Target => $"{Original.JobGroup}.{Original.JobName}";

    public string LevelIcon 
    {
        get => Original.EventId switch
        {
            106 or 143 => "✅ 啟用/建立",
            140 => "✏️ 更新",
            141 or 142 => "⏸️ 刪除/停用",
            _ => "ℹ️ 資訊"
        };
    }

    public string Category
    {
        get => Original.EventId switch
        {
            106 => "排程任務已註冊/建立",
            140 => "排程任務設定已修改",
            141 => "排程任務已刪除",
            142 => "排程任務已停用",
            143 => "排程任務已重新啟用",
            _ => "系統管理事件"
        };
    }
}
