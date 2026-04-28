namespace Scheduler.Core.Constants;

public static class SchedulerEventIds
{
    public const int WorkStarted = 100;
    public const int SchedulerTriggered = 107;
    public const int UserTriggered = 110;
    public const int ProcessCreated = 129;
    public const int ActionStarted = 200;
    public const int ActionCompleted = 201;
    public const int ActionFailed = 203;
    public const int SkippedByConcurrency = 322;
    public const int SkippedByWeeklyInterval = 323;
    public const int ActionStopped = 328;

    public const int JobCreated = 106;
    public const int JobUpdated = 140;
    public const int JobDeleted = 141;
    public const int JobPaused = 142;
    public const int JobResumed = 143;

    public static bool IsTriggerEvent(int eventId) => eventId is SchedulerTriggered or UserTriggered;

    public static bool IsProcessEvent(int eventId) => eventId is WorkStarted or SchedulerTriggered or UserTriggered or ProcessCreated or ActionStarted;
}
