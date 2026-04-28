using Scheduler.Core.Constants;

namespace Scheduler.Core.Services;

public static class JobLogDisplayMapper
{
    public static string? ToLastRunResult(int eventId, bool isSuccess, int? exitCode, string? errorMessage)
    {
        return eventId switch
        {
            SchedulerEventIds.ActionCompleted => $"成功執行 ({exitCode ?? 0})",
            SchedulerEventIds.ActionFailed => "執行失敗",
            SchedulerEventIds.SkippedByConcurrency => "依並發規則略過",
            SchedulerEventIds.SkippedByWeeklyInterval => "依每週間隔規則略過",
            SchedulerEventIds.ActionStopped => "已終止",
            _ when !isSuccess && !ContainsAny(errorMessage, "並發", "中斷", "週規則") => "執行失敗",
            _ when ContainsAny(errorMessage, "並發") => "依並發規則略過",
            _ when ContainsAny(errorMessage, "週規則") => "依每週間隔規則略過",
            _ when ContainsAny(errorMessage, "中斷") => "已終止",
            _ => null
        };
    }

    public static int NormalizeLegacyEventId(int eventId, bool isSuccess, string? errorMessage)
    {
        if (eventId != 0)
        {
            return eventId;
        }

        if (isSuccess)
        {
            return SchedulerEventIds.ActionCompleted;
        }

        if (ContainsAny(errorMessage, "強制中斷"))
        {
            return SchedulerEventIds.ActionStopped;
        }

        if (ContainsAny(errorMessage, "並發"))
        {
            return SchedulerEventIds.SkippedByConcurrency;
        }

        if (ContainsAny(errorMessage, "週規則"))
        {
            return SchedulerEventIds.SkippedByWeeklyInterval;
        }

        return SchedulerEventIds.ActionFailed;
    }

    public static string ToLevelText(int eventId)
    {
        return eventId switch
        {
            SchedulerEventIds.SkippedByConcurrency or SchedulerEventIds.SkippedByWeeklyInterval or SchedulerEventIds.ActionStopped => "警告",
            SchedulerEventIds.ActionFailed => "錯誤",
            _ => "資訊"
        };
    }

    public static string ToCategory(int eventId)
    {
        return eventId switch
        {
            SchedulerEventIds.UserTriggered => "使用者已經觸發工作",
            SchedulerEventIds.SchedulerTriggered => "排程器已觸發工作",
            SchedulerEventIds.ProcessCreated => "已建立工作處理程序",
            SchedulerEventIds.WorkStarted => "工作已開始",
            SchedulerEventIds.ActionStarted => "動作已經啟動",
            SchedulerEventIds.ActionCompleted => "動作已完成",
            SchedulerEventIds.SkippedByConcurrency => "啟動要求已遭忽略，因為執行個體已在執行中",
            SchedulerEventIds.SkippedByWeeklyInterval => "啟動要求已遭忽略，因為不符合每週間隔規則",
            SchedulerEventIds.ActionStopped => "動作已停止",
            _ => "動作失敗"
        };
    }

    public static string ToOpCode(int eventId)
    {
        return eventId switch
        {
            SchedulerEventIds.WorkStarted => "(1)",
            SchedulerEventIds.ActionCompleted => "(2)",
            SchedulerEventIds.SchedulerTriggered or SchedulerEventIds.UserTriggered or SchedulerEventIds.ProcessCreated
                or SchedulerEventIds.ActionStarted or SchedulerEventIds.SkippedByConcurrency
                or SchedulerEventIds.SkippedByWeeklyInterval or SchedulerEventIds.ActionStopped => "資訊",
            _ => "(1)"
        };
    }

    public static string ToDescription(int eventId, string jobName, string eventTime, string duration, int? exitCode, string? errorMessage)
    {
        return eventId switch
        {
            SchedulerEventIds.SkippedByConcurrency => $"工作排程器並未啟動工作 \"{jobName}\"，因為相同工作的執行個體已在執行中。",
            SchedulerEventIds.SkippedByWeeklyInterval => $"工作排程器並未啟動工作 \"{jobName}\"，因為本次觸發不符合每隔 N 週的執行規則。",
            SchedulerEventIds.ActionStopped => $"工作排程器已強迫停止工作 \"{jobName}\"，因為收到外部中止要求。",
            SchedulerEventIds.UserTriggered => $"使用者已經手動要求啟動工作 \"{jobName}\"。",
            SchedulerEventIds.SchedulerTriggered => $"工作排程器已針對工作 \"{jobName}\" 收到要求啟動的訊號。",
            SchedulerEventIds.ProcessCreated => $"工作排程器已為工作 \"{jobName}\" 建立執行個體處理程序。",
            SchedulerEventIds.WorkStarted => $"工作排程器已啟動工作 \"{jobName}\" 的執行個體。",
            SchedulerEventIds.ActionStarted => $"工作排程器動作已在工作 \"{jobName}\" 中啟動。",
            SchedulerEventIds.ActionCompleted => $"工作排程器於 {eventTime} 已成功完成工作 \"{jobName}\"，結束代碼：{exitCode}。\n執行耗時：{duration}。",
            _ => $"工作排程器於 {eventTime} 未能順利完成工作 \"{jobName}\"，因為執行緒或子程序回報失敗。這可能是因為找不到檔案、參數錯誤，或程式提早閃退。\n錯誤訊息：{errorMessage}\n執行耗時：{duration}。"
        };
    }

    private static bool ContainsAny(string? value, params string[] fragments)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        return fragments.Any(value.Contains);
    }
}
