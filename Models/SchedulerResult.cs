namespace SchoolDesk.Models;

public enum ScheduleState
{
    BeforeFirstEvent,
    InProgress,
    Transition,
    Free,
    Finished
}

public sealed record SchedulerResult(
    ScheduleState State,
    ScheduleEntry? Current,
    ScheduleEntry? Next,
    ScheduleEntry? NextLesson,
    DateTime? NextLessonDateTime,
    TimeSpan? Remaining,
    string Week,
    string Message);
