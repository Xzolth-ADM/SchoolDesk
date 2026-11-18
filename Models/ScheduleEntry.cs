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
    public string? Teacher { get; init; }
    public ScheduleKind Kind { get; init; }

    public bool AppliesTo(string week) => Week == "AB" || Week == week;

    public string DisplayName =>
        $"{DayLabel(Day)} · {Week} · {Start:HH\\:mm}–{End:HH\\:mm} · {Title}" +
        (string.IsNullOrWhiteSpace(Room) ? "" : $" · {Room}") +
        (string.IsNullOrWhiteSpace(Teacher) ? "" : $" · {Teacher}");

    public static string DayLabel(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Lundi",
        DayOfWeek.Tuesday => "Mardi",
        DayOfWeek.Wednesday => "Mercredi",
        DayOfWeek.Thursday => "Jeudi",
        DayOfWeek.Friday => "Vendredi",
        DayOfWeek.Saturday => "Samedi",
        DayOfWeek.Sunday => "Dimanche",
        _ => throw new ArgumentOutOfRangeException(nameof(day), day, "Jour inconnu.")
    };
}
