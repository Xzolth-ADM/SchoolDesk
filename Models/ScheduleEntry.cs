namespace SchoolDesk.Models;

public enum ScheduleKind
{
    Lesson,
    Break,
    Lunch,
    Free,
    HomeMeal
}

public sealed class ScheduleEntry
{
    public DayOfWeek Day { get; init; }
    public string Week { get; init; } = "AB";
    public TimeOnly Start { get; init; }
    public TimeOnly End { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Room { get; init; }
    public ScheduleKind Kind { get; init; }

    public bool AppliesTo(string week) => Week == "AB" || Week == week;
}
